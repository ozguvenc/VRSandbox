using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Plugins.SawceGames.QuickScenes
{
    public class ScenesWindow : EditorWindow, IHasCustomMenu
    {
        private struct SceneInfo
        {
            public string Name;
            public string Path;
        }
        
        /// <summary>
        /// Current active window.
        /// </summary>
        private static ScenesWindow current;

        /// <summary>
        /// Scene name character limit.
        /// </summary>
        private int maxSceneName = 32;

        /// <summary>
        /// Current scene names.
        /// </summary>
        private readonly List<SceneInfo> scenes = new List<SceneInfo>();

        /// <summary>
        /// Current scene names on display (can be filtered).
        /// </summary>
        private readonly List<SceneInfo> filteredScenes = new List<SceneInfo>();

        /// <summary>
        /// Points scene path to their names for all scenes.
        /// </summary>
        private readonly Dictionary<string, SceneInfo> allScenesMap = new Dictionary<string, SceneInfo>();

        /// <summary>
        /// Currently selected button grid element.
        /// </summary>
        private int selGridInt = -1;

        /// <summary>
        /// This holds all GUID to all projects scenes when loaded.
        /// </summary>
        private string[] allScenes;

        /// <summary>
        /// Remove the scene next click instead of going to it.
        /// </summary>
        private bool removeNextClick;

        /// <summary>
        /// If no scenes are found when filtering the scene set, but are found when searching all scenes,
        /// next click will add the scene.
        /// </summary>
        private bool addNextClick;

        /// <summary>
        /// When the user holds the alt key, this is set to true
        /// preventing scenes to be added.
        /// </summary>
        private bool dontAddScene;

        /// <summary>
        /// When the user holds the shift key, this is set to true
        /// preventing scene navigation.
        /// </summary>
        private bool dontNavigateToScene;

        private Vector2 sceneButtonsScrollPos;

        /// <summary>
        /// Variable to store the last filter used in the last filter operation.
        /// Used to prevent constant re filtering.
        /// </summary>
        private string lastFilter;

        /// <summary>
        /// This is set to true if the current filter text contains the '*' character.
        /// </summary>
        private bool filterIsSearch;
        
        /// <summary>
        /// Set to true if the current filter returned no scenes.
        /// </summary>
        private bool noScenesOnFilterResult;

        /// <summary>
        /// Debounce bool for remove scenes toggle.
        /// Set to true until the user releases ctrl.
        /// </summary>
        private bool toggledRemoveScenes;
        
        public ScenesWindow()
        {
            scenes.Clear();
            filteredScenes.Clear();

            current = this;
        }

        /// <summary>
        /// This is the window focus state.
        /// </summary>
        private bool IsFocused => (focusedWindow != null && focusedWindow.Equals(this));

        #region OPTIONS

        private string filter = "";

        #endregion OPTIONS

        #region SETTINGS

        private static bool detectSceneChanges = true;
        private readonly GUIContent detectSceneChangesItem = new GUIContent("Detect Scene Changes");

        private readonly GUIContent addAllScenesItem = new GUIContent("Add all scenes");
        private readonly GUIContent clearAllScenesItem = new GUIContent("Clear all scenes");

        /// <summary>
        /// Adds a custom window context menu.
        /// </summary>
        /// <param name="menu"></param>
        public void AddItemsToMenu(GenericMenu menu)
        {
            detectSceneChanges = EditorPrefs.GetBool(EDITOR_SAVE_KEY + "detectScenes", true);

            menu.AddItem(detectSceneChangesItem, detectSceneChanges, DetectMapToggle);
            menu.AddItem(addAllScenesItem, false, AddAllScenes);
            menu.AddItem(clearAllScenesItem, false, ClearAllScenes);
        }

        private static void DetectMapToggle()
        {
            detectSceneChanges = !detectSceneChanges;
            EditorPrefs.SetBool(EDITOR_SAVE_KEY + "detectScenes", detectSceneChanges);
        }

        private void ClearAllScenes()
        {
            scenes.Clear();
            filteredScenes.Clear();
            
            allScenesMap.Clear();
            
            SaveAndSetDirty();
        }

        /// <summary>
        /// Adds all scenes in the project to the button menu.
        /// </summary>
        private void AddAllScenes()
        {
            FindAllScenes();
            
            foreach (string guid in allScenes)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string sceneFileName = Path.GetFileName(path)?.Replace(".unity", "");

                if (string.IsNullOrEmpty(sceneFileName)) continue;
                if(scenes.Exists(s => s.Path == path)) continue;
                
                scenes.Add(new SceneInfo()
                {
                    Name = sceneFileName,
                    Path = path
                });
            }

            SaveAndSetDirty();
            BuildAllScenesMap();

            lastFilter = null;
        }

        #endregion SETTINGS

        #region STYLE

        private static GUIStyle sceneButtonStyle;
        private static readonly Color Light = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color LightRed = new Color(0.65f, 0.15f, 0.15f);
        private static readonly Color LightGreen = new Color(0.15f, 0.55f, 0.15f);

        #endregion STYLE

        #region SAVE AND LOAD
        
        private const string EDITOR_SAVE_KEY = "quickscenes_";
        private const string QUICKSCENES_SCENES_JSON = "QuickScenes/{name}.json";
        private const string SCENES_KEY = "current_scenes";

        #endregion SAVE AND LOAD

        [MenuItem("Window/Quick Scenes")]
        public static void ShowWindow()
        {
            GetWindow(typeof(ScenesWindow));
        }

        /// <summary>
        /// Accept drops anywhere.
        /// </summary>
        private void AcceptDrops()
        {
            Event evt = Event.current;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type != EventType.DragPerform) return;
            DragAndDrop.AcceptDrag();

            foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
            {
                SceneAsset scene = draggedObject as SceneAsset;

                if (scene == null) continue;
                string path = AssetDatabase.GetAssetPath(scene.GetInstanceID());
                scenes.Add(new SceneInfo() 
                {
                    Name = scene.name,
                    Path = path
                });
            }

            if(!allScenesMap.Any()) 
                BuildAllScenesMap();
            
            SaveAndSetDirty();
            
            //Necessary to cause filtering
            lastFilter = null;
        }

/*
    /// <summary>
    /// Boxed drop area.
    /// </summary>
    public void DropAreaGui() {
        Event evt = Event.current;

        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop scenes here");

        //Detect the correct events
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
        if (!dropArea.Contains(evt.mousePosition))
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type != EventType.DragPerform) return;
        DragAndDrop.AcceptDrag();

        foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
        {
            SceneAsset scene = draggedObject as SceneAsset;

            if (scene == null) continue;
            string path = AssetDatabase.GetAssetPath(scene.GetInstanceID());
            nameToPath[scene.name] = path;
        }

        BuildScenePathMap();
    }
*/

        /// <summary>
        /// Builds all buttons styles
        /// </summary>
        private static void ValidateStyle()
        {
            if (sceneButtonStyle != null) return;

            //Default
            sceneButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                wordWrap = true,
                margin = new RectOffset(3, 3, 3, 3),
                padding = new RectOffset(15, 15, 15, 15),
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                normal = {textColor = new Color(1, 1, 1)},
                fontStyle = FontStyle.Bold
            };
        }

        /// <summary>
        /// Saves current scenes and set editor dirty.
        /// </summary>
        private void SaveAndSetDirty()
        {
            //Save scenes
            SaveScenes();
            
            EditorUtility.SetDirty(this);
        }

        private void PrepareWindowTitle()
        {
            // Loads an icon from an image stored at the specified path
            Texture icon = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Plugins/SawceGames/QuickScenes/logo.png");

            // Create the instance of GUIContent to assign to the window. Gives the title "RBSettings" and the icon
            GUIContent content = new GUIContent("Quick Scenes", icon);
            titleContent = content;
        }

        private void OnEnable()
        {
            PrepareWindowTitle();
            
            LoadScenes();

            SaveAndSetDirty();

            FindAllScenes();
            BuildAllScenesMap();

            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.newSceneCreated += OnNewScene;
            
            //Reset state
            lastFilter = null;
        }

        /// <summary>
        /// Loads the scenes list saved on the system.
        /// </summary>
        private void LoadScenes()
        {
            scenes.Clear();
            
            var loadedScenes = ReadSerializableObject<List<SceneInfo>>(SCENES_KEY);
            if(loadedScenes != null)
                scenes.AddRange(loadedScenes);
        }
        
        /// <summary>
        /// Saves scenes list.
        /// </summary>
        private void SaveScenes()
        {
            SaveSerializableObject(SCENES_KEY, scenes);
        }

        private void OnNewScene(Scene scene, NewSceneSetup setup, NewSceneMode mode)
        {
            FindAllScenes();
            BuildAllScenesMap();
            lastFilter = null;
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            SaveScenes();
        }

        /// <summary>
        /// Automatic scene change addition.
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="mode"></param>
        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!detectSceneChanges && !addNextClick) return;
            
            if (!AddScene(scene)) return;
            if(!allScenesMap.Any()) BuildAllScenesMap();
            SaveAndSetDirty();
            
            //Necessary to cause filtering
            lastFilter = null;
        }

        /// <summary>
        /// Adds a scene to the current scenes list.
        /// Doesn't add the scene if there is a scene with the same path added.
        /// IF Don't add scene is set, this is also ignored.
        /// </summary>
        /// <param name="scene"></param>
        /// <returns>true if the scene was added</returns>
        private bool AddScene(Scene scene)
        {
            if (scenes.Exists(s => s.Path == scene.path)) return false;
            if (dontAddScene) return false;
            
            scenes.Add(new SceneInfo()
            {
                Name = scene.name,
                Path = scene.path
            });
            return true;
        }

        /// <summary>
        /// Applies filter and filter search.
        /// </summary>
        private void FilterScenes()
        {
            //Reset
            filterIsSearch = false;
            
            //Prevent filtering forever
            if (lastFilter == filter) return;
            lastFilter = filter;
            
            //Reset
            addNextClick = false;

            //Filter scenes
            if (!string.IsNullOrEmpty(filter))
            {
                //Finds scenes that contain the filter part
                filteredScenes.Clear();
                filteredScenes.AddRange(scenes
                    .Where(s => s.Name.ToLower().Contains(filter.ToLower()))
                    .ToArray()
                );

                filterIsSearch = filter.Contains("*");
                
                //If no scenes contain the filter, find all and filter on them
                if (filteredScenes.Count == 0 || filterIsSearch)
                {
                    filteredScenes.Clear();
                    filteredScenes.AddRange(
                        allScenesMap.Values
                            .Where(s => s.Name.ToLower().Contains(filter.Replace("*", "").ToLower()))
                            .ToArray()
                    );
                    
                    if (filteredScenes.Count > 0)
                    {
                        removeNextClick = false;
                        addNextClick = true;
                    }

                    noScenesOnFilterResult = true;
                }
                else
                {
                    noScenesOnFilterResult = false;
                }
            }
            else
            {
                filteredScenes.Clear();
                filteredScenes.AddRange(scenes);
            }
                

            //Limits all nam lengths
            //scenesNameDisplayLimited = LimitNames(filteredScenes);
            
            //Debug.Log("Filtered: "+filter);
        }

        #region GLOBAL KEY PRESS DETECTION

        [InitializeOnLoadMethod]
        private static void EditorInit()
        {
            System.Reflection.FieldInfo info =
                typeof(EditorApplication)
                    .GetField("globalEventHandler",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            if (info == null) return;
            EditorApplication.CallbackFunction value =
                (EditorApplication.CallbackFunction) info.GetValue(null);

            value += EditorGlobalKeyPress;

            info.SetValue(null, value);
        }

        private static void EditorGlobalKeyPress()
        {
            if (current == null) return;
            //Debug.Log("KEY CHANGE " + Event.current.keyCode);
            current.HandleInput();
        }

        private void HandleInput()
        {
            if (Event.current == null || 
                Event.current.type == EventType.Repaint
            ) return;
            
            dontAddScene = Event.current.alt;
            dontNavigateToScene = Event.current.shift;
            
            /*Debug.Log($"Event {Event.current.type} {Event.current.keyCode} " +
                      $"focused {IsFocused} isLeftControl {Event.current.keyCode == KeyCode.LeftControl}" +
                      $" noScenesOnFilterResult {noScenesOnFilterResult}");*/
            
            //Reset debounce
            if (Event.current.keyCode == KeyCode.LeftControl &&
                Event.current.type == EventType.KeyUp)
            {
                toggledRemoveScenes = false;
            }
            
            if (!toggledRemoveScenes &&
                Event.current.keyCode == KeyCode.LeftControl &&
                Event.current.type == EventType.KeyDown &&
                IsFocused && 
                (!noScenesOnFilterResult || string.IsNullOrEmpty(filter)))
            {
                //Debug.Log($"Control focused addNextClick {addNextClick} scenes.Count {scenes.Count}");
                if (!addNextClick || scenes.Count == 0)
                {
                    toggledRemoveScenes = true;
                    removeNextClick = !removeNextClick;
                    //Debug.Log($"removeNextClick {removeNextClick}");
                }
            }

            Repaint();
        }

        #endregion GLOBAL KEY PRESS DETECTION

        private void OnGUI()
        {
            ValidateStyle();
            AcceptDrops();

            filter = EditorGUILayout.TextField("Filter:", filter);

            //Box to make pretty
            if (scenes.Count > 0 || filteredScenes.Any())
                EditorGUILayout.BeginVertical("Box");
            else
                EditorGUILayout.BeginVertical();

            //Scroll layout if needed
            int count = 1;
            if (filteredScenes.Any())
                count = Mathf.CeilToInt(filteredScenes.Count / 3f);
            int height = (count > 1 ? 48 : 49) * count;
            
            //Min height if at least one scene
            if(filteredScenes.Any()){
                height = Mathf.Max(50, height);
                height = Mathf.Min(250, height);
            }
            
            sceneButtonsScrollPos =
                EditorGUILayout.BeginScrollView(sceneButtonsScrollPos, false, false,
                    GUILayout.Height(height));

            //Does the scene filtering logic
            FilterScenes();

            //Handle keys and mouse inputs
            HandleInput();

            //Grid colors
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = Light;
            if (removeNextClick)
            {
                GUI.backgroundColor = LightRed;
            }
            else
            {
                if (addNextClick && !dontAddScene)
                {
                    GUI.backgroundColor = LightGreen;
                }
            }

            //Calc max button size
            float width = (position.width - 20);
            maxSceneName = (position.width < 310 ? (position.width < 260 ? 8 : 16) : 32);

            //The button grid
            selGridInt = GUILayout.SelectionGrid(
                selGridInt, ConvertToGuiContent(filteredScenes), 3,
                sceneButtonStyle, GUILayout.MaxWidth(width), GUILayout.MinWidth(200));

            //Restore grid tint
            GUI.backgroundColor = prevColor;

            //The button grid action
            if (selGridInt >= 0)
            {
                SceneButtonClicked();
                
                //Reset the last filter so we can go back to the scene listing
                lastFilter = null;
                //Reset the selected scene
                selGridInt = -1;
            }

            //Scroll layout
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();

            if (!addNextClick || scenes.Count == 0)
                EditorGUILayout.BeginHorizontal("Box");

            if (scenes.Count > 0)
            {
                if (!addNextClick || scenes.Count == 0)
                    removeNextClick = GUILayout.Toggle(removeNextClick, "Remove scenes");
            }
            else
            {
                GUILayout.Label("Drop scenes here or");

                if (GUILayout.Button("Add all scenes"))
                {
                    AddAllScenes();
                }

                removeNextClick = false;
            }

            if (!addNextClick || scenes.Count == 0)
            {
                EditorGUILayout.EndHorizontal();
            }

            if (dontNavigateToScene)
            {
                EditorGUILayout.LabelField("Shift being held. Won't navigate to the scene when clicked.");
            }

            if (dontAddScene)
            {
                EditorGUILayout.LabelField("Alt being held. Won't add the scene to the current set.");
            }

            if (filterIsSearch)
            {
                EditorGUILayout.LabelField("Filter contains '*'. All scenes will be searched. Search mode is locked.");
            }
        }

        private void SceneButtonClicked()
        {
            //If there is nothing to remove, prevent removal
            if (removeNextClick && scenes.Count == 0)
            {
                removeNextClick = false;
            }
 
            SceneInfo scene = filteredScenes[selGridInt];
            
            //Check if scene still exists, if it doesn't remove it
            bool sceneNotPresent = false;
            if (!File.Exists(scene.Path))
            {
                sceneNotPresent = true;
                Debug.LogWarning($"The scene at {scene.Path} was removed.");
            }
            
            if (removeNextClick || sceneNotPresent)
            {
                scenes.Remove(scene);
                SaveAndSetDirty();
            }
            else
            {
                //If can navigate
                if (dontNavigateToScene)
                {
                    //Add the scene
                    scenes.Add(allScenesMap[scene.Path]);

                    SaveAndSetDirty();
                    return;
                }

                //Check save
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    //If filter mode, open from the all scenes map
                    EditorSceneManager.OpenScene(
                        addNextClick
                            ? allScenesMap[scene.Path].Path
                            : scene.Path, OpenSceneMode.Single);
                }
            }
        }

        /// <summary>
        /// Finds all scenes in the project and stores them
        /// to the local allScenes array.
        /// </summary>
        private void FindAllScenes()
        {
            allScenes = AssetDatabase.FindAssets("t:Scene");
        }

        /// <summary>
        /// Builds the allScenesMap dictionary.
        /// For each asset GUID in the all scenes string array, this
        /// method finds the scene path, finds the file name and removes the .unity from it
        /// adding it to the sceneFilename.
        /// If the resulting file name is not empty, it is added to the all scenes map mapping:
        /// 
        /// scene path -> sceneInfo
        ///
        /// </summary>
        private void BuildAllScenesMap()
        {
            if(allScenes == null || allScenes.Length == 0)
                FindAllScenes();
            
            foreach (string guid in allScenes)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string sceneFilename = Path.GetFileName(path)?.Replace(".unity", "");

                if (string.IsNullOrEmpty(sceneFilename)) continue;
                if (!allScenesMap.ContainsKey(sceneFilename))
                {
                    allScenesMap[path] = new SceneInfo()
                    {
                        Name = sceneFilename,
                        Path = path
                    };
                }
            }
        }

        /// <summary>
        /// Limits each entry string length to maxSceneName characters or less.
        /// Converts each entry to a gui content with the scene name as text and
        /// scene path as tooltip.
        /// </summary>
        /// <param name="toLimit"></param>
        /// <returns></returns>
        private GUIContent[] ConvertToGuiContent(IEnumerable<SceneInfo> toLimit)
        {
            return toLimit
                .Select(v =>
                    new GUIContent
                    {
                        text = v.Name.Substring(0, Math.Min(v.Name.Length, maxSceneName)),
                        tooltip = v.Path
                    }
                )
                .ToArray();
        }

        #region SERIALIZATION
        private static T ReadSerializableObject<T>(string objectName) where T : class
        {
            string currentFilePath = 
                QUICKSCENES_SCENES_JSON.Replace("{name}", objectName);

            if (!File.Exists(currentFilePath))
            {
                Debug.Log($"No such file: {currentFilePath}");
                return null;
            }

            string fileData = File.ReadAllText(currentFilePath);
            T obj = JsonConvert.DeserializeObject<T>(fileData);
            return obj;
        }
        
        private static void SaveSerializableObject<T>(string objectName, T toSave)
        {
            CheckAndCreateDirectory();

            string currentFilePath = QUICKSCENES_SCENES_JSON.Replace("{name}", objectName);
            
            //Remove current file if exists
            if (File.Exists(currentFilePath))
            {
                File.Delete(currentFilePath);
            }
            
            //Save list
            string serialized = JsonConvert.SerializeObject(toSave);
            File.WriteAllText(currentFilePath, serialized);
        }

        private static void CheckAndCreateDirectory()
        {
            if (!Directory.Exists("QuickScenes"))
            {
                Directory.CreateDirectory("QuickScenes");
            }
        }

        #endregion SERIALIZATION
    }
}

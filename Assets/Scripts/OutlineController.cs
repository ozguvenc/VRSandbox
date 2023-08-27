using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Outline))]
public class OutlineController : MonoBehaviour
{
    [SerializeField]
    private string allowedInteractorTag = "RightHand";
    private XRBaseInteractable interactable;

    private Outline outline;

    private void Awake()
    {
        outline = GetComponent<Outline>();
        interactable = GetComponent<XRBaseInteractable>();

        if (interactable)
        {
            interactable.hoverEntered.AddListener(HoverEntered);
            interactable.hoverExited.AddListener(HoverExited);
        }
    }

    private void HoverEntered(HoverEnterEventArgs args)
    {
        if (args.interactor.CompareTag(allowedInteractorTag))
        {
            outline.enabled = true;
        }
    }

    private void HoverExited(HoverExitEventArgs args)
    {
        if (args.interactor.CompareTag(allowedInteractorTag))
        {
            outline.enabled = false;
        }
    }
}

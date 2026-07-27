using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class TutorialInteractable : MonoBehaviour
{
    [Header("Referencias")]
    public GameObject tutorialSign;
    public GameObject tutorialPanel;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;

    private bool tutorialShown = false;

    void Awake()
    {
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
    }

    void OnEnable()
    {
        grab.selectEntered.AddListener(OnGrab);
    }

    void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnGrab);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        if (tutorialShown)
            return;

        tutorialShown = true;

        if (tutorialSign != null)
            tutorialSign.SetActive(false);

        if (tutorialPanel != null)
            tutorialPanel.SetActive(true);
    }
}
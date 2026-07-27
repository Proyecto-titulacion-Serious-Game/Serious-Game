using UnityEngine;
using UnityEngine.InputSystem;

public class TutorialPanel : MonoBehaviour
{
    [Header("Input Trigger")]
    public InputActionReference triggerAction;

    private bool waitingInput;

    void OnEnable()
    {
        waitingInput = true;

        if (triggerAction != null)
            triggerAction.action.Enable();
    }

    void Update()
    {
        if (!waitingInput)
            return;

        if (triggerAction != null &&
            triggerAction.action.WasPressedThisFrame())
        {
            waitingInput = false;
            gameObject.SetActive(false);
        }
    }
}
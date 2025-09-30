using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class GameTimer : NetworkBehaviour
{
    [SerializeField]
    TextMeshProUGUI timerText;

    NetworkVariable<float> remainingTime = new NetworkVariable<float>(0f, writePerm: NetworkVariableWritePermission.Server);
    NetworkVariable<bool> isActive = new NetworkVariable<bool>(false, writePerm: NetworkVariableWritePermission.Server);


    bool isInterrupted = false;

    private void Awake()
    {
        timerText.enabled = false;
    }

    private void OnEnable()
    {
        remainingTime.OnValueChanged += UpdateTimerText;
        isActive.OnValueChanged += UpdateVisibility;
    }


    private void OnDisable()
    {
        remainingTime.OnValueChanged -= UpdateTimerText;
        isActive.OnValueChanged -= UpdateVisibility;
    }

    public void SetVisibility(bool enabled)
    {
        if (NetworkManager.Singleton.IsHost)
        {
            isActive.Value = enabled;
        }
    }

    [ServerRpc]
    public void StartTimerServerRpc(float duration)
    {
        StartTimerInternal(duration);
    }

    [ServerRpc]
    public void InterruptTimerServerRpc()
    {
        StopTimerImmediate();
    }

    void StartTimerInternal(float duration)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        StopAllCoroutines();
        isInterrupted = false;
        remainingTime.Value = duration;
        SetVisibility(true);
        StartCoroutine(CountDownCoroutine());
    }

    public void StopTimerImmediate()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        isInterrupted = true;
        StopAllCoroutines();
        remainingTime.Value = 0f;
        SetVisibility(false);
    }

    IEnumerator CountDownCoroutine()
    {
        while (remainingTime.Value > 0 && !isInterrupted)
        {
            yield return new WaitForSeconds(1f);
            remainingTime.Value--;
        }

        SetVisibility(false);
    }

    private void UpdateTimerText(float previousValue, float newValue)
    {
        TimeSpan t = TimeSpan.FromSeconds(newValue);
        timerText.text = $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    private void UpdateVisibility(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            timerText.enabled = true;
        }
        else
        {
            timerText.enabled = false;
        }
    }
}

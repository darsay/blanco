using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Collections;

public class UIManagerNet : MonoBehaviour
{
    public static UIManagerNet Instance;

    public TMP_Text cartaText;
    public GameObject votacionPanel;
    public TMP_Text resultadoText;
    public TMP_Text temporizadorText;
    public Button repartirCartasButton;

    void Awake()
    {
        Instance = this;
        if (repartirCartasButton != null)
            repartirCartasButton.onClick.AddListener(OnRepartirCartasClicked);
    }

    void Start()
    {
        print(Unity.Netcode.NetworkManager.Singleton.IsServer);
        if (!Unity.Netcode.NetworkManager.Singleton.IsServer && repartirCartasButton != null)
            repartirCartasButton.gameObject.SetActive(false);
    }

    public void ShowRepartir()
    {
        if (repartirCartasButton != null)
            repartirCartasButton.gameObject.SetActive(true);
    }

    public void MostrarCarta(FixedString32Bytes carta)
    {
        Debug.Log($"Carta mostrada: {carta.ToString()}");
        Debug.Log(cartaText);
        cartaText.text = $"Tu carta: {carta.ToString()}";
    }


    public void EnviarPalabra(TMP_InputField input)
    {
        string palabra = input.text;
        GameManager.Instance.EnviarPalabraServerRpc(palabra);
    }

    public void VotarJugador(ulong id)
    {
        GameManager.Instance.EnviarVotoServerRpc(id);
        votacionPanel.SetActive(false);
    }

    public void MostrarResultado(bool acertaron, string votado, string impostor)
    {
        resultadoText.text = acertaron
            ? $"✅ Adivinaste. {votado} era el impostor."
            : $"❌ Fallaste. El impostor era {impostor}.";
    }

    void OnRepartirCartasClicked()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.RepartirCartas();
    }

    public void ActualizarTemporizador(float tiempo)
    {
       temporizadorText.text = Mathf.CeilToInt(tiempo).ToString(); // por ejemplo
    }

    public void MostrarPanelVotacion()
    {
        // Mostrar botones o UI para votar por jugadores
    }

}

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Netcode;

public class UIManagerNet : MonoBehaviour
{
    public static UIManagerNet Instance;

    public TMP_Text cartaText;
    public GameObject votacionPanel;
    public TMP_Text resultadoText;
    public TMP_Text temporizadorText;
    public Button repartirCartasButton;
    public GameObject botonVotoPrefab; // Prefab del botón de voto

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
        resultadoText.text = "WAITING FOR PLAYERS TO VOTE"; // Limpiar resultado anterior
    }

    public void VotarJugador(ulong id)
    {
        GameManager.Instance.EnviarVotoServerRpc(id);
        votacionPanel.SetActive(false);
    }

    public void Empate(string empate_text)
    {
        resultadoText.text = empate_text;
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

    public void MostrarPanelVotacion(List<ulong> jugadores)
    {
        Debug.Log("MostrarPanelVotacion");
        // Limpia botones anteriores
        foreach (Transform child in votacionPanel.transform)
            Destroy(child.gameObject);

        // Crea un botón por cada jugador
        foreach (ulong id in jugadores)
        {
            var botonObj = Instantiate(botonVotoPrefab, votacionPanel.transform);
            var texto = botonObj.GetComponentInChildren<TMPro.TMP_Text>();
            texto.text = $"Jugador {id}";
            var boton = botonObj.GetComponent<Button>();
            ulong idCopia = id; // Necesario para la closure
            boton.onClick.AddListener(() => VotarJugador(idCopia));
        }

        votacionPanel.SetActive(true);
    }

}

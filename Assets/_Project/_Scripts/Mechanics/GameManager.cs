using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    public enum Estado : byte { EsperandoJugadores, RepartiendoCartas, EnviandoPalabras, Votando, Resultado }

    public NetworkVariable<Estado> estadoActual = new NetworkVariable<Estado>(Estado.EsperandoJugadores);
    public NetworkVariable<int> impostorId = new NetworkVariable<int>(-1);

    private Dictionary<ulong, FixedString32Bytes> palabrasRecibidas = new();
    private Dictionary<ulong, ulong> votosRecibidos = new();
    private Dictionary<ulong, FixedString32Bytes> cartasPorJugador = new();

    [Header("Cartas")]
    public List<FixedString32Bytes> cartasDisponibles = new List<FixedString32Bytes>
    {
        "Perro", "Gato", "Pájaro", "Conejo", "Tortuga", "Ratón", "Loro", "Hamster"
    };

    public FixedString32Bytes cartaComun = "";
    public FixedString32Bytes cartaImpostor = "";

    public int jugadoresNecesarios = 4;
    public UIManagerNet uiManager;

    [Header("Temporizador")]
    public float tiempoVotacion = 30f; // duración en segundos
    public float tiempoRestante = 0f;
    public bool votacionActiva = false;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            UIManagerNet.Instance.ShowRepartir();
        }
    }

    void OnClienteConectado(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"Jugador conectado: {clientId}");

        if (NetworkManager.ConnectedClients.Count == jugadoresNecesarios)
        {
            estadoActual.Value = Estado.EsperandoJugadores;
            // Ya no llamamos a ComenzarJuego aquí
        }
    }

    void ComenzarJuego()
    {
        if (!IsServer) return;
        estadoActual.Value = Estado.RepartiendoCartas;

        List<ulong> jugadores = new List<ulong>(NetworkManager.ConnectedClientsIds);
        impostorId.Value = (int)jugadores[Random.Range(0, jugadores.Count)];

        cartaComun = cartasDisponibles[Random.Range(0, cartasDisponibles.Count)];
        cartaImpostor = "BLANCO";
        // MODO COOPERATIVO:
        /*
        cartaImpostor = cartasDisponibles[Random.Range(0, cartasDisponibles.Count)];
         while (cartaImpostor.Equals(cartaComun))
              {
         cartaImpostor = cartasDisponibles[Random.Range(0, cartasDisponibles.Count)];
           }
        */
        NetworkManager.OnClientConnectedCallback += OnClienteConectado;
        UIManagerNet.Instance.ShowRepartir();
        


            foreach (ulong id in jugadores)
        {
            FixedString32Bytes carta = (id == (ulong)impostorId.Value) ? cartaImpostor : cartaComun;
            cartasPorJugador[id] = carta;
            Debug.Log($"Jugador {id} recibe carta: {carta.ToString()}");
            EnviarCartaClientRpc(carta, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { id } }
            });
        }

        estadoActual.Value = Estado.EnviandoPalabras;
    }

    // Cambiar esto, no se dicen las palabras, es todo oral.
    [ServerRpc(RequireOwnership = false)]
    public void EnviarPalabraServerRpc(string palabra, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!palabrasRecibidas.ContainsKey(clientId))
        {
            palabrasRecibidas[clientId] = palabra;
            Debug.Log($"Jugador {clientId} dijo: {palabra}");

            if (palabrasRecibidas.Count == NetworkManager.ConnectedClientsIds.Count)
            {
                estadoActual.Value = Estado.Votando;

                tiempoRestante = tiempoVotacion;
                votacionActiva = true;
                UIManagerNet.Instance.MostrarPanelVotacion();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void EnviarVotoServerRpc(ulong votadoId, ServerRpcParams rpcParams = default)
    {
        ulong votanteId = rpcParams.Receive.SenderClientId;

        if (!votosRecibidos.ContainsKey(votanteId))
        {
            votosRecibidos[votanteId] = votadoId;
            Debug.Log($"Jugador {votanteId} votó por {votadoId}");

            if (votosRecibidos.Count == NetworkManager.ConnectedClientsIds.Count)
            {
                FinalizarRonda();
            }
        }
    }

    void FinalizarRonda()
    {
        estadoActual.Value = Estado.Resultado;

        // Contar votos
        Dictionary<ulong, int> conteo = new();
        foreach (ulong votado in votosRecibidos.Values)
        {
            if (!conteo.ContainsKey(votado))
                conteo[votado] = 0;

            conteo[votado]++;
        }

        // Buscar el más votado
        ulong masVotado = 0;
        int max = -1;

        foreach (var kvp in conteo)
        {
            if (kvp.Value > max)
            {
                max = kvp.Value;
                masVotado = kvp.Key;
            }
        }

        bool acertaron = (masVotado == (ulong)impostorId.Value);
        string nombreImpostor = "Jugador " + impostorId.Value;
        string nombreVotado = "Jugador " + masVotado;

        MostrarResultadoClientRpc(acertaron, nombreVotado, nombreImpostor);

        // Reiniciar juego tras 10 segundos
        Invoke(nameof(ReiniciarJuego), 10f);
    }

    void ReiniciarJuego()
    {
        cartasPorJugador.Clear();
        palabrasRecibidas.Clear();
        votosRecibidos.Clear();
        impostorId.Value = -1;
        estadoActual.Value = Estado.EsperandoJugadores;
    }

    // ---------- RPCs hacia CLIENTES ----------

    [ClientRpc]
    void EnviarCartaClientRpc(FixedString32Bytes carta, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"Enviando carta a cliente: {carta.ToString()}");
        UIManagerNet.Instance.MostrarCarta(carta);
    }


    [ClientRpc]
    void MostrarResultadoClientRpc(bool acertaron, string votado, string impostor)
    {
        UIManagerNet.Instance.MostrarResultado(acertaron, votado, impostor);
    }

    public void RepartirCartas()
    {
        if (!IsServer) return;
        ComenzarJuego();
    }

    void Update()
    {
        if (!IsServer || !votacionActiva || estadoActual.Value != Estado.Votando)
            return;

        tiempoRestante -= Time.deltaTime;

        // Opcional: informar a los clientes cuánto tiempo queda
        ActualizarTemporizadorClientRpc(tiempoRestante);

        if (tiempoRestante <= 0f)
        {
            votacionActiva = false;
            FinalizarRonda();
        }
    }

    [ClientRpc]
    void ActualizarTemporizadorClientRpc(float tiempoRestante)
    {
        UIManagerNet.Instance.ActualizarTemporizador(tiempoRestante);
    }

}
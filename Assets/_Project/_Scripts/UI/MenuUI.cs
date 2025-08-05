using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;

namespace Blanco.UI
{
    public class MenuUI : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject createLobbyPanel;
        [SerializeField] private GameObject joinLobbyPanel;
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private GameObject errorPanel;
        
        [Header("Main Panel")]
        [SerializeField] private Button createLobbyButton;
        [SerializeField] private Button joinLobbyButton;
        
        [Header("Player Name")]
        [SerializeField] private TMP_InputField playerNameInput;
        [SerializeField] private TextMeshProUGUI currentPlayerNameText;
        
        [Header("Create Lobby Panel (No usado)")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button backFromCreateButton;
        [SerializeField] private TextMeshProUGUI lobbyCodeText;
        [SerializeField] private Button copyCodeButton;
        
        [Header("Join Lobby Panel")]
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button backFromJoinButton;
        
        [Header("Loading Panel")]
        [SerializeField] private TextMeshProUGUI loadingText;
        
        [Header("Error Panel")]
        [SerializeField] private TextMeshProUGUI errorText;
        [SerializeField] private Button closeErrorButton;
        
        private Blanco.Networking.LobbyManager lobbyManager;
        private bool isProcessing = false;
        
        private void Start()
        {
            // Buscar o crear LobbyManager
            lobbyManager = FindObjectOfType<Blanco.Networking.LobbyManager>();
            if (lobbyManager == null)
            {
                GameObject lobbyManagerObj = new GameObject("LobbyManager");
                lobbyManager = lobbyManagerObj.AddComponent<Blanco.Networking.LobbyManager>();
                DontDestroyOnLoad(lobbyManagerObj);
            }
            
            // Configurar botones del panel principal
            if (createLobbyButton != null)
                createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);
            
            if (joinLobbyButton != null)
                joinLobbyButton.onClick.AddListener(OnJoinLobbyClicked);
            
            // Configurar botones del panel crear lobby (no usado por ahora)
            // if (startButton != null)
            //     startButton.onClick.AddListener(OnStartGameClicked);
            // 
            // if (backFromCreateButton != null)
            //     backFromCreateButton.onClick.AddListener(OnBackFromCreateClicked);
            // 
            // if (copyCodeButton != null)
            //     copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
            
            // Configurar botones del panel unirse
            if (joinButton != null)
                joinButton.onClick.AddListener(OnJoinButtonClicked);
            
            if (backFromJoinButton != null)
                backFromJoinButton.onClick.AddListener(OnBackFromJoinClicked);
            
            // Configurar botón de error
            if (closeErrorButton != null)
                closeErrorButton.onClick.AddListener(OnCloseErrorClicked);
            
            // Cargar datos guardados
            LoadSavedData();
            
            // Mostrar panel principal
            ShowMainPanel();
        }
        
        private void LoadSavedData()
        {
            // Cargar código del lobby si existe
            string savedLobbyCode = PlayerPrefs.GetString("LobbyCode", "");
            if (!string.IsNullOrEmpty(savedLobbyCode) && codeInput != null)
            {
                codeInput.text = savedLobbyCode;
            }
            
            // Cargar nombre del jugador guardado
            LoadPlayerName();
        }
        
        private void LoadPlayerName()
        {
            string savedName = PlayerPrefs.GetString("PlayerName", "");
            if (currentPlayerNameText != null)
            {
                if (!string.IsNullOrEmpty(savedName))
                {
                    currentPlayerNameText.text = $"Jugador: {savedName}";
                }
                else
                {
                    currentPlayerNameText.text = "Jugador: Sin nombre";
                }
            }
            
            // Configurar input field
            if (playerNameInput != null)
            {
                playerNameInput.text = savedName;
                playerNameInput.onEndEdit.AddListener(OnPlayerNameChanged);
            }
        }
        
        private void OnPlayerNameChanged(string newName)
        {
            if (!string.IsNullOrEmpty(newName))
            {
                PlayerPrefs.SetString("PlayerName", newName);
                PlayerPrefs.Save();
                
                if (currentPlayerNameText != null)
                {
                    currentPlayerNameText.text = $"Jugador: {newName}";
                }
                
                Debug.Log($"✅ Nombre guardado: {newName}");
            }
        }
        
        private void ShowMainPanel()
        {
            mainPanel?.SetActive(true);
            createLobbyPanel?.SetActive(false);
            joinLobbyPanel?.SetActive(false);
            loadingPanel?.SetActive(false);
            errorPanel?.SetActive(false);
        }
        
        // private void ShowCreateLobbyPanel()
        // {
        //     mainPanel?.SetActive(false);
        //     createLobbyPanel?.SetActive(true);
        //     joinLobbyPanel?.SetActive(false);
        //     loadingPanel?.SetActive(false);
        //     errorPanel?.SetActive(false);
        // }
        
        private void ShowJoinLobbyPanel()
        {
            mainPanel?.SetActive(false);
            createLobbyPanel?.SetActive(false);
            joinLobbyPanel?.SetActive(true);
            loadingPanel?.SetActive(false);
            errorPanel?.SetActive(false);
        }
        
        private void ShowLoadingPanel(string message = "Cargando...")
        {
            mainPanel?.SetActive(false);
            createLobbyPanel?.SetActive(false);
            joinLobbyPanel?.SetActive(false);
            loadingPanel?.SetActive(true);
            errorPanel?.SetActive(false);
            
            if (loadingText != null)
                loadingText.text = message;
        }
        
        private void ShowErrorPanel(string error)
        {
            mainPanel?.SetActive(false);
            createLobbyPanel?.SetActive(false);
            joinLobbyPanel?.SetActive(false);
            loadingPanel?.SetActive(false);
            errorPanel?.SetActive(true);
            
            if (errorText != null)
                errorText.text = error;
        }
        
        private async void OnCreateLobbyClicked()
        {
            if (isProcessing) return;
            
            isProcessing = true;
            ShowLoadingPanel("Creando lobby...");
            
            try
            {
                // Generar nombre del lobby
                string lobbyName = GenerateLobbyName();
                
                // Crear lobby
                bool success = await lobbyManager.CreateLobby(lobbyName);
                
                if (success)
                {
                    // Cambiar a escena de lobby directamente
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
                }
                else
                {
                    ShowErrorPanel("❌ Error al crear el lobby. Inténtalo de nuevo.");
                    isProcessing = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error al crear lobby: {e.Message}");
                ShowErrorPanel($"❌ Error: {e.Message}");
                isProcessing = false;
            }
        }
        
        private void OnJoinLobbyClicked()
        {
            ShowJoinLobbyPanel();
        }
        
        // private void OnBackFromCreateClicked()
        // {
        //     ShowMainPanel();
        // }
        
        private void OnBackFromJoinClicked()
        {
            ShowMainPanel();
        }
        
        private void OnCloseErrorClicked()
        {
            ShowMainPanel();
        }
        
        // private void OnCopyCodeClicked()
        // {
        //     string lobbyCode = PlayerPrefs.GetString("LobbyCode", "");
        //     if (!string.IsNullOrEmpty(lobbyCode))
        //     {
        //         GUIUtility.systemCopyBuffer = lobbyCode;
        //         Debug.Log($"✅ Código copiado: {lobbyCode}");
        // }
        // }
        
        // private async void OnStartGameClicked()
        // {
        //     if (isProcessing) return;
        //     
        //     isProcessing = true;
        //     ShowLoadingPanel("Creando lobby...");
        //     
        //     try
        //     {
        //         // Generar nombre del lobby
        //         string lobbyName = GenerateLobbyName();
        //         
        //         // Crear lobby
        //         bool success = await lobbyManager.CreateLobby(lobbyName);
        //         
        //         if (success)
        //         {
        //             // Mostrar código del lobby
        //             string lobbyCode = PlayerPrefs.GetString("LobbyCode", "");
        //             if (lobbyCodeText != null)
        //                 lobbyCodeText.text = $"Código: {lobbyCode}";
        //             
        //             // Cambiar a escena de lobby
        //             UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        //         }
        //         else
        //         {
        //             ShowErrorPanel("❌ Error al crear el lobby. Inténtalo de nuevo.");
        //             isProcessing = false;
        //         }
        //     }
        //     catch (System.Exception e)
        //     {
        //         Debug.LogError($"Error al crear lobby: {e.Message}");
        //         ShowErrorPanel($"❌ Error: {e.Message}");
        //         ShowErrorPanel($"❌ Error: {e.Message}");
        //         isProcessing = false;
        //     }
        // }
        
        private async void OnJoinButtonClicked()
        {
            if (isProcessing) return;
            
            // Validar código del lobby
            string lobbyCode = "";
            if (codeInput != null)
            {
                lobbyCode = codeInput.text.Trim();
            }
            
            if (string.IsNullOrEmpty(lobbyCode))
            {
                ShowErrorPanel("❌ Por favor ingresa un código de lobby válido.");
                return;
            }
            
            isProcessing = true;
            ShowLoadingPanel("Uniéndose al lobby...");
            
            try
            {
                // Guardar código del lobby
                PlayerPrefs.SetString("LobbyCode", lobbyCode);
                PlayerPrefs.Save();
                
                // Unirse al lobby
                bool success = await lobbyManager.JoinLobby(lobbyCode);
                
                if (success)
                {
                    // Cambiar a escena de lobby (se sincronizará con el host)
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
                }
                else
                {
                    ShowErrorPanel("❌ Error al unirse al lobby. Verifica el código e inténtalo de nuevo.");
                    isProcessing = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error al unirse al lobby: {e.Message}");
                ShowErrorPanel($"❌ Error: {e.Message}");
                isProcessing = false;
            }
        }
        
        private string GenerateLobbyName()
        {
            // Generar número aleatorio
            int randomNumber = Random.Range(1000, 9999);
            
            return $"Lobby-{randomNumber}";
        }
        
        [ContextMenu("Clear PlayerPrefs")]
        public void ClearPlayerPrefs()
        {
            PlayerPrefs.DeleteKey("LobbyCode");
            PlayerPrefs.DeleteKey("LobbyId");
            PlayerPrefs.DeleteKey("PlayerName");
            PlayerPrefs.Save();
            
            if (codeInput != null)
                codeInput.text = "";
            
            if (playerNameInput != null)
                playerNameInput.text = "";
            
            LoadPlayerName();
            
            Debug.Log("✅ PlayerPrefs limpiados");
        }
    }
} 
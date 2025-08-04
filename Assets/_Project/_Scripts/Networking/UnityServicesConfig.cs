using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;
using System.Threading.Tasks;
using System;

#if UNITY_EDITOR
using ParrelSync;
#endif

namespace Blanco.Networking
{
    public class UnityServicesConfig : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;
        
        private void Start()
        {
            // Inicializar servicios automáticamente
            _ = InitializeServicesAsync();
        }
        
        private async Task InitializeServicesAsync()
        {
            try
            {
                if (showDebugLogs)
                    Debug.Log("🔧 Inicializando Unity Services...");
                
                await InitializeUnityServices();
                
                if (showDebugLogs)
                    Debug.Log("✅ Unity Services inicializados correctamente");
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error al inicializar Unity Services: {e.Message}");
            }
        }
        
        private async Task InitializeUnityServices()
        {
            var initializationOptions = new InitializationOptions();
            
#if UNITY_EDITOR
            if (ClonesManager.IsClone())
            {
                string customArgument = ClonesManager.GetArgument();
                initializationOptions.SetProfile($"Clone_{customArgument}_Profile");
                if (showDebugLogs)
                    Debug.Log($"🆔 Configurando perfil único para ParrelSync: Clone_{customArgument}_Profile");
            }
            else
            {
                initializationOptions.SetProfile("Primary");
                if (showDebugLogs)
                    Debug.Log("🆔 Configurando perfil primario");
            }
#endif
            
            await UnityServices.InitializeAsync(initializationOptions);
        }
        
        public string GetServicesInfo()
        {
            string info = "🔍 === INFO DE SERVICIOS ===\n";
            info += $"Estado de servicios: {UnityServices.State}\n";
            info += $"Autenticado: {AuthenticationService.Instance.IsSignedIn}\n";
            
            if (AuthenticationService.Instance.IsSignedIn)
            {
                info += $"Player ID: {AuthenticationService.Instance.PlayerId}\n";
            }
            
#if UNITY_EDITOR
            info += $"ParrelSync Clone: {ClonesManager.IsClone()}\n";
            if (ClonesManager.IsClone())
            {
                info += $"Argumento: {ClonesManager.GetArgument()}\n";
            }
#endif
            
            info += "🔍 === FIN INFO ===";
            return info;
        }
        
        [ContextMenu("Debug Services Info")]
        public void DebugServicesInfo()
        {
            Debug.Log(GetServicesInfo());
        }
        
        [ContextMenu("Test Authentication")]
        public async void TestAuthentication()
        {
            bool success = await Authentication.Login();
            Debug.Log($"Test de autenticación: {(success ? "✅ Exitoso" : "❌ Falló")}");
        }
    }
} 
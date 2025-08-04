using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using System.Threading.Tasks;
using System;
using Unity.Multiplayer.Playmode;
using System.Linq;
using Unity.Services.Lobbies.Models;

#if UNITY_EDITOR
using ParrelSync;
#endif

namespace Blanco.Networking
{
    public static class Authentication
    {
        private static int instanceCounter = 0;
        private static string uniqueInstanceId;
        
        public static async Task<bool> Login()
        {
            try
            {               
                // Limpieza agresiva antes de autenticar
                ClearAuthenticationCache();
                
                // Forzar sign out si ya está inicializado
                if (UnityServices.State == ServicesInitializationState.Initialized)
                {
                    AuthenticationService.Instance.SignOut();
                }
                
                await InitializeUnityServices();
                
                // Forzar sign out nuevamente después de inicializar
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    AuthenticationService.Instance.SignOut();
                }
                
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await SignIn();
                }

                if (UnityServices.State == ServicesInitializationState.Initialized)
                {
                    Debug.Log("✅ Autenticación exitosa");
                    return true;
                }
                else
                {
                    Debug.LogError("❌ Error en la inicialización de servicios");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error en autenticación: {e.Message}");
                return false;
            }
        }

        private static async Task InitializeUnityServices()
        {
            var initializationOptions = new InitializationOptions();

#if UNITY_EDITOR
            // Detectar si es ParrelSync o Multiplayer Play Mode
            if (ClonesManager.IsClone()) 
            {
                // ParrelSync
                initializationOptions.SetProfile(ClonesManager.GetArgument());
                Debug.Log($"🆔 Configurando perfil para ParrelSync: {ClonesManager.GetArgument()}");
            }
            else 
            {
                // Multiplayer Play Mode 
                var mppmTag = CurrentPlayer.ReadOnlyTags();
                if (mppmTag.Contains("auth"))
                {
                    var playerProfile = "Player" + UnityEngine.Random.Range(0, 100);
                    AuthenticationService.Instance.SwitchProfile(playerProfile);
                    initializationOptions.SetProfile(playerProfile);
                    Debug.Log($"🆔 Configurando perfil para Multiplayer Play Mode: {playerProfile} (ID: {uniqueInstanceId})");
                }
            }
#endif

            await UnityServices.InitializeAsync(initializationOptions);
        }

        private static async Task SignIn()
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log($"✅ Sign in anónimo exitoso! Player ID: {AuthenticationService.Instance.PlayerId}");
            }
            else
            {
                Debug.LogError("❌ Sign in anónimo falló");
            }
        }
        
        public static string GetPlayerId()
        {
            return AuthenticationService.Instance.PlayerId;
        }
        
        public static bool IsSignedIn()
        {
            return AuthenticationService.Instance.IsSignedIn;
        }
        
        [ContextMenu("Debug Authentication Info")]
        public static void DebugAuthenticationInfo()
        {
            Debug.Log("🔍 === INFO DE AUTENTICACIÓN ===");
            Debug.Log($"🔍 IsSignedIn: {AuthenticationService.Instance.IsSignedIn}");
            Debug.Log($"🔍 PlayerId: {AuthenticationService.Instance.PlayerId}");
            Debug.Log($"🔍 UniqueInstanceId: {uniqueInstanceId}");
            Debug.Log($"🔍 InstanceCounter: {instanceCounter}");
            Debug.Log("🔍 === FIN INFO ===");
        }
        
        [ContextMenu("Clear Authentication Cache")]
        public static void ClearAuthenticationCache()
        {
            // Limpiar TODOS los PlayerPrefs relacionados con Unity Services
            PlayerPrefs.DeleteKey("Unity.Services.Authentication.PlayerId");
            PlayerPrefs.DeleteKey("Unity.Services.Authentication.AccessToken");
            PlayerPrefs.DeleteKey("Unity.Services.Authentication.RefreshToken");
            PlayerPrefs.DeleteKey("Unity.Services.Authentication.ExpirationTime");
            
            // Limpiar también los relacionados con Lobbies
            PlayerPrefs.DeleteKey("Unity.Services.Lobbies.LobbyId");
            PlayerPrefs.DeleteKey("Unity.Services.Lobbies.LobbyCode");
            
            // Limpiar cualquier otro caché de Unity Services
            var keys = new string[] {
                "Unity.Services.Authentication.PlayerId",
                "Unity.Services.Authentication.AccessToken", 
                "Unity.Services.Authentication.RefreshToken",
                "Unity.Services.Authentication.ExpirationTime",
                "Unity.Services.Lobbies.LobbyId",
                "Unity.Services.Lobbies.LobbyCode",
                "Unity.Services.Relay.AllocationId"
            };
            
            foreach (var key in keys)
            {
                PlayerPrefs.DeleteKey(key);
            }
            
            PlayerPrefs.Save();
            
            // Forzar sign out
            if (AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut();
            }
            
            // Resetear variables estáticas
            uniqueInstanceId = null;
            instanceCounter = 0;
            
            Debug.Log("🧹 Caché de autenticación completamente limpiado");
        }
        

    }
} 
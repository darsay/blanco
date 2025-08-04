using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using System.Threading.Tasks;

#if UNITY_EDITOR
using ParrelSync;
#endif

namespace Blanco.Networking
{
    public static class Authentication
    {
        public static async Task<bool> Login()
        {
            try
            {               
                if (UnityServices.State == ServicesInitializationState.Initialized)
                {
                    AuthenticationService.Instance.SignOut();
                }
                
                await InitializeUnityServices();
                
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
            if (ClonesManager.IsClone()) 
            {
                initializationOptions.SetProfile(ClonesManager.GetArgument());
                Debug.Log($"🆔 Configurando perfil para ParrelSync: {ClonesManager.GetArgument()}");
            }
            else 
            {
                initializationOptions.SetProfile("Primary");
                Debug.Log("🆔 Configurando perfil primario");
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
    }
} 
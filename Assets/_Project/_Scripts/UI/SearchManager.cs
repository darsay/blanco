using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class SearchManager : MonoBehaviour
{
    // 
    // tart is called once before the first execution of Update after the MonoBehaviour is created

    private HashSet<ClassDataBack> set = new HashSet<ClassDataBack>();

    [SerializeField]
    ClassNamePanel ClassNamePrefab;

    [SerializeField]
    Transform ClassNamesContainer;

    [SerializeField]
    TMP_InputField searchText;


    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void Search()
    {
        Debug.Log("PRESSED");
        StartCoroutine(SearchCoroutine(searchText.text));
    }

    IEnumerator SearchCoroutine(string search) //, System.Action<List<ClassDataBack>> onResults
    {
        // Reemplaza la URL por la de tu API
        // https://68e914a2f2707e6128cd7bf5.mockapi.io/blanko/api/:endpoint
        // string url = $"https://tu-api.com/busqueda?query={UnityWebRequest.EscapeURL(search)}";
        string url = $"https://68e914a2f2707e6128cd7bf5.mockapi.io/blanko/api/Classes?name={UnityWebRequest.EscapeURL(search)}";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            Debug.Log("Search pressed");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Error en la petición: " + request.error);
            }
            else
            {
                Debug.Log("Respuesta de la API: " + request.downloadHandler.text);
                ClassDataBack[] results = JsonHelper.FromJson<ClassDataBack>(request.downloadHandler.text);

                // Reemplaza esta línea:
                // UpdatePalabras(results);
                // Por esta línea:
                set = new HashSet<ClassDataBack>(results);
                UpdatePalabras(new List<ClassDataBack>(results));
                //      onResults?.Invoke(new List<ClassDataBack>(results));
            }
        }
    }

    void UpdatePalabras(List<ClassDataBack> palabras)
    {
        // Aquí debes actualizar tu VerticalPanel con los datos recibidos
        // Por ejemplo, crear elementos UI para cada palabra

        Debug.Log("Updating palabras");
        ClearPlayerList();

        foreach (var palabra in palabras)
        {

            Debug.Log($"Palabra: {palabra.name}, Desc: {palabra.name}, URL: {palabra.Description}");
            AddNewPanel(palabra);
            // Aquí puedes instanciar un prefab o actualizar un componente UI
        }

    }


    public void AddNewPanel(ClassDataBack class_data)
    {
        var namePanel = Instantiate(ClassNamePrefab, ClassNamesContainer);
        namePanel.SetPanel(class_data);
    }

    public void ClearPlayerList()
    {
        if (ClassNamesContainer != null)
        {
            foreach (Transform child in ClassNamesContainer)
            {
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }

    // Helper para deserializar arrays con JsonUtility
    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            string newJson = "{\"array\":" + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.array;
        }

        [System.Serializable]
        private class Wrapper<T>
        {
            public T[] array;
        }
    }
}

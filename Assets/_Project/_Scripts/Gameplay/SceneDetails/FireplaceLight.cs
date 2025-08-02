using UnityEngine;

[RequireComponent(typeof(Light))]
public class FireplaceLight : MonoBehaviour
{
    [Header("Intensity Settings")]
    public float minIntensity = 0.8f;
    public float maxIntensity = 1.2f;
    public float flickerSpeed = 5f;

    [Header("Color Settings")]
    public Color baseColor = new Color(1f, 0.5f, 0.1f); // Anaranjado
    public float colorVariation = 0.05f;

    private Light candleLight;
    private float targetIntensity;

    void Start()
    {
        candleLight = GetComponent<Light>();
        candleLight.color = baseColor;
        targetIntensity = candleLight.intensity;
    }

    void Update()
    {
        // Cambiar intensidad suavemente
        targetIntensity = Random.Range(minIntensity, maxIntensity);
        candleLight.intensity = Mathf.Lerp(candleLight.intensity, targetIntensity, Time.deltaTime * flickerSpeed);

        // Variación de color sutil
        float r = baseColor.r + Random.Range(-colorVariation, colorVariation);
        float g = baseColor.g + Random.Range(-colorVariation * 0.5f, colorVariation * 0.5f); // Menos verde
        float b = baseColor.b + Random.Range(-colorVariation * 0.3f, colorVariation * 0.3f); // Menos azul
        candleLight.color = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
    }
}

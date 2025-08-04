# Configuración de Unity Services

## Paso 1: Configurar Unity Services en el Editor

1. **Abrir Unity Services:**
   - Ve a `Window > General > Services`
   - O ve a `Project Settings > Services`

2. **Activar Unity Services:**
   - Haz clic en "Activate Unity Services"
   - Inicia sesión con tu cuenta de Unity

3. **Configurar Project ID:**
   - Copia el Project ID que aparece en la ventana de Services
   - Este ID es único para tu proyecto

## Paso 2: Configurar Relay Service

1. **Activar Relay:**
   - En la ventana de Services, busca "Relay"
   - Haz clic en "Activate"
   - Acepta los términos de servicio

2. **Configurar Relay:**
   - Ve a la pestaña "Relay" en Services
   - Verifica que esté activado

## Paso 3: Configurar Authentication Service

1. **Activar Authentication:**
   - En la ventana de Services, busca "Authentication"
   - Haz clic en "Activate"
   - Acepta los términos de servicio

2. **Configurar Authentication:**
   - Ve a la pestaña "Authentication" en Services
   - Verifica que esté activado

## Paso 4: Actualizar el código

Una vez que tengas el Project ID, actualiza el archivo `UnityServicesConfig.cs`:

```csharp
[SerializeField] private string projectId = "TU-PROJECT-ID-AQUI";
```

## Paso 5: Verificar configuración

1. **Ejecutar el juego**
2. **Verificar logs** - Deberías ver:
   - "Unity Services inicializados correctamente"
   - "Usuario autenticado: [ID]"
   - "Servicios de Unity listos"

## Solución de problemas

Si sigues viendo errores de "Bad Request":
1. Verifica que el Project ID sea correcto
2. Asegúrate de que Relay esté activado
3. Espera unos minutos después de activar los servicios
4. Reinicia Unity si es necesario 
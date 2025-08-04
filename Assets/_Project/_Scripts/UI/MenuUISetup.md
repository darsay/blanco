# Configuración de la UI del Menú Principal

## Estructura de Paneles

### 1. Main Panel (Panel Principal)
- **Objetos necesarios:**
  - `mainPanel` (GameObject)
  - `createLobbyButton` (Button)
  - `joinLobbyButton` (Button)

### 2. Create Lobby Button (Botón de Crear Lobby)
- **Objetos necesarios:**
  - `createLobbyButton` (Button) - Botón para crear el lobby directamente

### 3. Join Panel (Panel de Unirse)
- **Objetos necesarios:**
  - `joinPanel` (GameObject)
  - `joinCodeInput` (TMP_InputField) - Campo para introducir el código
  - `joinButton` (Button) - Botón para unirse al lobby
  - `backFromJoinButton` (Button) - Botón para volver al panel principal

### 4. Loading Panel (Panel de Carga)
- **Objetos necesarios:**
  - `loadingPanel` (GameObject)
  - `loadingText` (TextMeshProUGUI) - Texto de carga

### 5. Error Panel (Panel de Error)
- **Objetos necesarios:**
  - `errorPanel` (GameObject)
  - `errorText` (TextMeshProUGUI) - Texto del error
  - `errorCloseButton` (Button) - Botón para cerrar el error

## Pasos para Configurar en Unity

### Paso 1: Crear la Estructura de Paneles

1. **Crear el Canvas principal:**
   ```
   Canvas (UI > Canvas)
   ├── Main Panel
   ├── Join Panel
   ├── Loading Panel
   └── Error Panel
   ```

2. **Configurar cada panel:**
   - Todos los paneles deben ser GameObjects hijos del Canvas
   - Cada panel debe tener un `RectTransform`
   - Solo el `Main Panel` debe estar activo inicialmente

### Paso 2: Configurar el Main Panel

1. **Crear botones:**
   ```
   Main Panel
   ├── Create Lobby Button
   └── Join Lobby Button
   ```

2. **Configurar textos:**
   - "Crear Lobby" para `createLobbyButton`
   - "Unirse a Lobby" para `joinLobbyButton`



### Paso 3: Configurar el Join Panel

1. **Crear elementos:**
   ```
   Join Panel
   ├── Title Text ("Unirse a Lobby")
   ├── Join Code Input (placeholder: "Código del lobby")
   ├── Join Button ("Unirse")
   └── Back Button ("Volver")
   ```

2. **Configurar input field:**
   - Character Limit: 6
   - Character Validation: Alphanumeric
   - Placeholder: "Código del lobby"

### Paso 4: Configurar Loading y Error Panels

1. **Loading Panel:**
   ```
   Loading Panel
   ├── Loading Text ("Cargando...")
   └── Spinner (opcional)
   ```

2. **Error Panel:**
   ```
   Error Panel
   ├── Error Text ("")
   └── Close Button ("Cerrar")
   ```

### Paso 5: Asignar Referencias

1. **Seleccionar el GameObject con MenuUI script**
2. **En el Inspector, asignar cada referencia:**
   - Arrastrar cada panel a su campo correspondiente
   - Arrastrar cada botón a su campo correspondiente
   - Arrastrar cada texto a su campo correspondiente

## Flujo de Navegación

### Flujo Normal:
1. **Main Panel** → Usuario ve opciones
2. **Create Lobby Button** → Usuario presiona crear lobby
3. **Loading Panel** → Mientras se crea
4. **Lobby Scene** → Al crear lobby exitosamente (código se muestra en la escena de lobby)

### Flujo de Unirse:
1. **Main Panel** → Usuario ve opciones
2. **Join Panel** → Usuario introduce código
3. **Loading Panel** → Mientras se une
4. **Lobby Scene** → Al unirse exitosamente

## Solución de Problemas

### Botones no funcionan:
1. Verificar que las referencias estén asignadas en el Inspector
2. Verificar que los botones tengan `Button` component
3. Verificar que los `onClick` events estén configurados

### Paneles no se muestran:
1. Verificar que los GameObjects estén activos
2. Verificar que las referencias estén asignadas
3. Verificar que el script `MenuUI` esté en el GameObject correcto

### Textos no se actualizan:
1. Verificar que los `TextMeshProUGUI` estén asignados
2. Verificar que los textos tengan contenido inicial
3. Verificar que los GameObjects estén activos

### Debug:
- Usar la consola de Unity para ver logs
- Verificar que el script `MenuUI` esté en el GameObject correcto
- Revisar que las referencias estén asignadas en el Inspector

## Notas Importantes

- **Este script es para el MENÚ PRINCIPAL**, no para la escena de lobby
- **Los paneles se usan para crear/unirse a lobbies**
- **Después de crear/unirse, se cambia a la escena "Lobby"**
- **La escena de lobby tiene su propio script `LobbySceneUI`** 
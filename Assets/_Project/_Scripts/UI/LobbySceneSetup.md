# Configuración de la Escena de Lobby

## Objetos Necesarios

### 1. Canvas Principal
- **Canvas** con `CanvasScaler` configurado para `Scale With Screen Size`
- **Canvas Group** para control de visibilidad

### 2. Panel de Lobby
- **Panel** (Image) como contenedor principal
- **Layout Group** (Vertical Layout Group) para organizar elementos

### 3. Información del Lobby
- **Text - TMP** para mostrar el código del lobby
  - Asignar a `lobbyCodeText` en `LobbySceneUI`
  - Texto: "Código: XXXXXX"
  - Estilo: Destacado, fácil de leer

### 4. Botón Copiar Código
- **Button** para copiar el código
  - Asignar a `copyCodeButton` en `LobbySceneUI`
  - Texto: "Copiar Código"
  - Evento: `CopyLobbyCode()`

### 5. Lista de Jugadores
- **Scroll View** para la lista de jugadores
  - **Viewport** con **Content** (Vertical Layout Group)
  - Asignar `Content` a `playerListContent` en `LobbyManager`
  - Asignar `Content` a `playerListContent` en `LobbySceneUI`

### 6. Prefab de Item de Jugador
- **Panel** con información del jugador
- **Text - TMP** para nombre del jugador
- **Image** para indicador de host (opcional)
- **PlayerListItem** script
- Asignar a `playerListItemPrefab` en `LobbyManager`

### 7. Información de Jugadores
- **Text - TMP** para contador de jugadores
  - Asignar a `playerCountText` en `LobbySceneUI`
  - Texto: "Jugadores: X/Y"

### 8. Botones de Control
- **Button** "Iniciar Juego" (solo visible para host)
  - Asignar a `startGameButton` en `LobbyManager`
  - Asignar a `startGameButton` en `LobbySceneUI`
  - Evento: `StartGame()`

- **Button** "Salir del Lobby"
  - Asignar a `leaveLobbyButton` en `LobbySceneUI`
  - Evento: `LeaveLobby()`

### 9. Texto de Espera
- **Text - TMP** "Esperando al host..."
  - Asignar a `waitingForHostText` en `LobbyManager`
  - Asignar a `waitingForHostText` en `LobbySceneUI`
  - Solo visible para clientes

## Componentes Necesarios

### 1. LobbySceneUI
- Script principal para manejar la UI del lobby
- Configurar todas las referencias de UI

### 2. LobbyManager (CRÍTICO)
- Script de red para manejar el estado del lobby
- **DEBE estar en la escena**
- **DEBE tener todas las referencias asignadas**
- **DEBE ser NetworkBehaviour**

### 3. LobbyConnectionManager
- Script para manejar la conexión de red
- Debe estar en la escena

## Configuración de la Escena

### 1. Build Settings
- Agregar "Lobby" a las escenas en Build Settings
- Orden: MainMenu → Lobby → Gameplay

### 2. Configuración de Red
- Asegurar que `LobbyManager` y `LobbyConnectionManager` estén en la escena
- Verificar que `NetworkManager` esté configurado

### 3. UI Layout
- Usar **Canvas Scaler** con `Scale With Screen Size`
- **Reference Resolution**: 1920x1080
- **Match**: 0.5 (ancho y alto)

## Debug

### Logs a Revisar
- "=== LOBBY MANAGER NETWORK SPAWN ===" al cargar la escena
- "🟢 Host iniciado" o "🔵 Cliente iniciado"
- "➕ Agregando jugador" cuando se agregan jugadores
- "📊 GetPlayerCount: X jugadores" para verificar contador
- "🔄 Actualizando lista de jugadores" para verificar UI

### Problemas Comunes
1. **LobbyManager no encontrado**: Verificar que esté en la escena
2. **playerListContent null**: Asignar el Content del ScrollView
3. **playerListItemPrefab null**: Crear y asignar el prefab
4. **0/4 jugadores**: Verificar que se agreguen jugadores correctamente

## Flujo de Uso

1. **Host crea lobby** → va a escena Lobby
2. **Código se muestra** en la UI
3. **Host puede copiar código** con el botón
4. **Clientes se unen** usando el código
5. **Lista se actualiza** en tiempo real
6. **Host inicia juego** cuando esté listo 
# Camera Inspector — MVP de descubrimiento y diagnóstico de cámaras IP

Camera Inspector es una aplicación de escritorio para Windows orientada a técnicos de CCTV. Su objetivo es descubrir dispositivos en red, identificar cámaras/IP devices, consultar ONVIF, resolver streams RTSP y, progresivamente, incorporar diagnóstico y administración.

## Estado actual

La aplicación ya incorpora:

- Descubrimiento de interfaces de red.
- Cálculo de subred y ping sweep paralelo.
- Resolución ARP para obtener MAC cuando está disponible.
- Resolución de fabricante mediante OUI, HTTP y ONVIF.
- WS-Discovery para detectar dispositivos ONVIF y obtener su `Device Service XAddr`.
- `GetDeviceInformation` para fabricante, modelo, firmware y número de serie.
- `GetCapabilities` para descubrir Media, Imaging, PTZ y Events.
- `GetProfiles` + `GetStreamUri` para resolver Main Stream y Sub Stream.
- Identificación de resolución, codec y FPS del perfil de video.
- Interfaz WPF con tabla de dispositivos y panel técnico.

## Requisitos

- Windows 10/11.
- .NET 9 SDK.
- Para desarrollo: Visual Studio 2022 o VS Code con el SDK de .NET 9.

La solución utiliza componentes específicos de Windows para ARP y WPF, por lo que la ejecución objetivo es Windows.

## Compilar

```bash
dotnet restore
dotnet build CameraInspector.sln
```

## Ejecutar

```bash
dotnet run --project CameraInspector.App
```

## Persistencia

La aplicación utiliza SQLite mediante Entity Framework Core. La base local se almacena en el perfil local del usuario para evitar requerir un servidor de base de datos.

## Arquitectura

```text
CameraInspector.App
        ↓
CameraInspector.Core
        ↓
CameraInspector.Network
        ↓
ONVIF / WS-Discovery / RTSP
        ↓
CameraInspector.Persistence
```

El núcleo debe permanecer desacoplado de WPF, SQLite y protocolos concretos.

## Próximos bloques

1. Visor RTSP dentro de la aplicación.
2. Diagnóstico automático de conectividad, autenticación y video.
3. Credential Manager de Windows.
4. Providers de fabricantes (Hikvision, Dahua, Axis, etc.).
5. Configuración ONVIF: imagen, red, PTZ, eventos.
6. Historial, auditoría y reportes.
7. Soporte USB/UVC como funcionalidad secundaria.

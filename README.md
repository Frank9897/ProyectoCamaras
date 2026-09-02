# Camera Inspector — descubrimiento y diagnóstico de cámaras IP

Camera Inspector es una aplicación de escritorio para Windows orientada a técnicos de CCTV. Su objetivo es descubrir dispositivos en red, identificar cámaras incluso cuando no usan ONVIF, resolver streams, ejecutar diagnóstico y mantener un inventario local.

## Estado actual

La aplicación incorpora varias capas independientes de detección y las fusiona por IP antes de presentarlas en la interfaz:

- Descubrimiento de interfaces IPv4 activas.
- Cálculo de subred y ping sweep paralelo.
- Resolución ARP para obtener MAC cuando está disponible.
- ONVIF / WS-Discovery.
- Descubrimiento propietario VIVOTEK, incluyendo redes APIPA `169.254.0.0/16`.
- Hikvision SADP y Dahua DHIP.
- SSDP / UPnP.
- mDNS / Bonjour para Axis, VAPIX y servicios HTTP/HTTPS/RTSP anunciados.
- Sondeo TCP de puertos frecuentes de cámaras y NVR.
- Fingerprint RTSP mediante `OPTIONS`.
- Fingerprint HTTP de fabricantes y equipos legacy.
- Detección de endpoints HTTP de imagen/JPEG/MJPEG.
- Detección por OUI de MAC como evidencia débil.
- Evidencia acumulativa con método, confianza y detalle.
- Deduplicación de resultados por IP.

La interfaz muestra el motivo de detección, por ejemplo:

```text
192.168.1.20   VIVOTEK Discovery + TCP/RTSP + HTTP banner
192.168.1.31   WS-Discovery + TCP/RTSP
192.168.1.40   mDNS/Bonjour
192.168.1.51   GenericVideoHttp + TCP/RTSP
```

Tener un puerto HTTP abierto no es suficiente para clasificar un dispositivo como cámara. La aplicación intenta acumular evidencias independientes antes de considerarlo un candidato de vídeo.

## Fabricantes y protocolos

### Integración de descubrimiento real

- ONVIF / WS-Discovery
- VIVOTEK discovery
- Hikvision SADP
- Dahua DHIP
- Axis mDNS / Bonjour
- SSDP / UPnP

### Identificación y fingerprint

- VIVOTEK
- Hikvision
- Dahua
- Axis
- Hanwha / Wisenet
- Uniview
- MOBOTIX
- Reolink
- cámaras RTSP genéricas
- cámaras HTTP/MJPEG antiguas o propietarias

Los fingerprints HTTP y RTSP no sustituyen a los protocolos propietarios. Cuando un protocolo de descubrimiento específico no está suficientemente documentado, el proyecto evita enviar paquetes experimentales que puedan producir falsos positivos o comportamientos inesperados.

## Arquitectura

```text
CameraInspector.App
        ↓
CameraInspector.Core
        ↓
CameraInspector.Network
        ├── Ping / ARP
        ├── ONVIF / WS-Discovery
        ├── VIVOTEK Discovery
        ├── Hikvision SADP
        ├── Dahua DHIP
        ├── SSDP / UPnP
        ├── mDNS / Bonjour
        ├── TCP Port Scan
        └── HTTP / RTSP fingerprints
        ↓
CameraInspector.Persistence
        ↓
SQLite local + Windows Credential Manager
```

El núcleo permanece desacoplado de WPF, SQLite y protocolos concretos. Los detectores de fabricante implementan `IManufacturerDetector` y sus resultados pasan por `ManufacturerResolver`.

## Evidencia de detección

Cada `DiscoveredDevice` puede conservar varias evidencias simultáneas. La UI puede mostrar:

- método de detección;
- confianza estimada;
- detalle técnico;
- si la señal constituye evidencia de cámara.

Esto permite investigar casos en los que una cámara no responde a ONVIF, pero sí aparece por un protocolo propietario, mDNS, RTSP o HTTP/MJPEG.

## Video y diagnóstico

La aplicación también incluye:

- resolución de perfiles ONVIF;
- Main Stream y Sub Stream;
- identificación de resolución, codec y FPS;
- reproducción mediante LibVLC;
- diagnóstico de conectividad/autenticación/video;
- historial de diagnósticos;
- persistencia del inventario;
- almacenamiento de credenciales mediante Windows Credential Manager.

## Requisitos

- Windows 10/11.
- .NET 9 SDK.
- Visual Studio 2022 o VS Code con .NET 9.

El proyecto utiliza componentes específicos de Windows para WPF y ARP, por lo que la ejecución objetivo es Windows.

## Compilar

```bash
dotnet restore
dotnet build CameraInspector.sln
dotnet test CameraInspector.sln
```

## Ejecutar

```bash
dotnet run --project CameraInspector.App
```

## Prueba recomendada en red real

Para una prueba con cámaras de trabajo conviene comparar dos escenarios:

1. **ESCANEAR RED** con la interfaz LAN seleccionada.
2. Conectar una cámara conocida directamente y revisar si aparece por VIVOTEK, ONVIF, RTSP, HTTP o varias fuentes simultáneamente.

En particular, una VIVOTEK antigua como la IP7134 es útil para comprobar el descubrimiento propietario y el comportamiento de cámaras sin ONVIF.

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

Tener un puerto HTTP, RTSP, ARP, SSDP u OUI abierto no es suficiente por sí solo para clasificar un dispositivo como cámara. El clasificador separa señales fuertes de señales débiles y exige corroboración antes de presentar determinados candidatos.

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
- fingerprints de endpoints remotos cuando presentan una identidad inequívoca de cámara

Los fingerprints HTTP y RTSP no sustituyen a los protocolos propietarios. Cuando un protocolo de descubrimiento específico no está suficientemente documentado, el proyecto evita enviar paquetes experimentales que puedan producir falsos positivos o comportamientos inesperados.

## Salud del dispositivo

Cada cámara puede pasar por una comprobación rápida independiente del descubrimiento. La aplicación diferencia entre:

- `OK`: comunicación y vídeo confirmados;
- `AUTENTICACIÓN`: el dispositivo responde pero solicita credenciales para vídeo;
- `ALERTA · SIN VIDEO`: existe comunicación/evidencia de cámara, pero no se confirmó vídeo;
- `ALERTA · SIN RESPUESTA`: no se encontraron puertos habituales disponibles;
- `ALERTA · DEGRADADA` y otros estados operativos cuando corresponda.

El diagnóstico revisa varios puertos TCP habituales, RTSP y endpoints de imagen HTTP/HTTPS. Para HTTPS se contempla el uso frecuente de certificados autofirmados en cámaras durante la comprobación de salud.

## Video y operaciones

La aplicación incluye una ventana independiente para cada cámara IP con:

- Main Stream y Sub Stream;
- reproducción mediante LibVLC;
- snapshot y grabación;
- información de streams;
- credenciales;
- diagnóstico de salud;
- PTZ cuando el servicio ONVIF correspondiente está disponible;
- Imaging, Events e información del proveedor;
- apertura de la interfaz web;
- configuración de red ONVIF cuando el dispositivo la expone.

Las ventanas secundarias son redimensionables, cuentan con desplazamiento cuando el contenido lo necesita y adaptan sus límites al monitor y a su DPI. La interfaz principal también reajusta la distribución de la lista y el detalle en resoluciones reducidas.

## Conexión remota

Existe un módulo de `CONEXIÓN DE ENLACE` para probar un host + puerto remoto e identificar automáticamente HTTP, HTTPS, RTSP o TCP.

La función remota puede devolver el endpoint como dispositivo cuando el fingerprint contiene identidad de cámara suficientemente fuerte. No se presupone que cualquier VMS, NVR, proxy o gateway permita enumerar las cámaras que tiene detrás: esa enumeración depende del protocolo de administración/exportación que exponga el equipo remoto.

## Seguridad de credenciales

Las contraseñas de cámaras no se almacenan en SQLite como texto plano. La aplicación utiliza Windows Credential Manager para conservar las credenciales y guarda en la base local únicamente la referencia y los datos necesarios para asociarlas al dispositivo.

Las operaciones sensibles, como cambios de red, reinicio o restauración de fábrica, pasan por la capa de configuración y sus validaciones correspondientes.

## Evidencia de detección

Cada `DiscoveredDevice` puede conservar varias evidencias simultáneas. La UI puede mostrar:

- método de detección;
- confianza estimada;
- detalle técnico;
- si la señal constituye evidencia de cámara.

Esto permite investigar casos en los que una cámara no responde a ONVIF, pero sí aparece por un protocolo propietario, mDNS, RTSP o HTTP/MJPEG.

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
        ├── Health diagnostics
        └── HTTP / HTTPS / RTSP fingerprints
        ↓
CameraInspector.Persistence
        └── SQLite + referencia de credenciales

CameraInspector.Video
        └── LibVLC / reproducción y captura
```

El núcleo permanece desacoplado de WPF, SQLite y protocolos concretos. Los detectores de fabricante implementan `IManufacturerDetector` y sus resultados pasan por `ManufacturerResolver`.

## Rendimiento y comportamiento de escaneo

El escaneo utiliza operaciones concurrentes con límites de tiempo bajos para evitar que una IP lenta bloquee toda la búsqueda. Los puertos candidatos se consultan en paralelo y la interfaz puede mostrar una IP descubierta antes de terminar todo el enriquecimiento del dispositivo.

En la ventana principal se exponen tres alcances de trabajo para que el técnico elija el menor impacto necesario:

- **Cámara directa**: prueba una IP indicada o utiliza descubrimiento apropiado para un equipo conectado directamente.
- **Red normal**: revisa la subred de la interfaz seleccionada.
- **Red completa**: repite el escaneo de subred sobre cada interfaz IPv4 activa elegible.

El panel superior también presenta el diagnóstico local de la interfaz elegida: IP, gateway y DNS. Puede actualizarse tras conectar un cable, cambiar de Wi-Fi o habilitar un adaptador, sin reiniciar la aplicación.

El escaneo directo con IP explícita evita barrer innecesariamente toda la subred. Cuando no se especifica IP en modo directo, la aplicación prioriza descubrimientos adecuados en lugar de forzar un barrido completo.

## Diagnóstico y ficha técnica

La ficha de cada cámara reúne identificación, estado de salud, evidencias, servicios ONVIF y endpoints anunciados. La pestaña **DIAGNÓSTICO** ejecuta una batería sin credenciales de ping, HTTP/HTTPS cuando corresponde, RTSP, información ONVIF y capacidades ONVIF. El resultado distingue una capacidad no anunciada de un fallo de comunicación y muestra el detalle de Media, Imaging, PTZ y Events cuando el equipo responde.

## Requisitos

- Windows 10/11.
- .NET 9 SDK.
- Visual Studio 2022 o VS Code con .NET 9.

El proyecto utiliza componentes específicos de Windows para WPF y ARP, por lo que la ejecución objetivo es Windows.

## Compilar

```bash
dotnet restore CameraInspector.sln
dotnet build CameraInspector.sln --configuration Release
dotnet test CameraInspector.Tests\CameraInspector.Tests.csproj --configuration Release
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

## CI y mantenimiento

El repositorio ejecuta compilación y pruebas automáticas sobre Windows con .NET 9 para evitar que una regresión de código llegue a `main` sin ser detectada.

Los artefactos locales de compilación (`bin`, `obj`, resultados de pruebas y archivos de IDE) están excluidos mediante `.gitignore` para que el código fuente y la configuración permanezcan separados de la salida generada.

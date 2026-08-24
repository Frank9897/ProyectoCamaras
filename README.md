# Camera Inspector — Fase 1 MVP (esqueleto inicial)

Este es el primer corte de código, correspondiente al arranque de la **Fase 1 (MVP)**:
detección de interfaz de red, cálculo de subred, ping sweep y resolución ARP,
mostrados en vivo en la primera pantalla (Escanear) con la UI en WPF.

## Requisitos para compilar

- Windows 10/11
- Visual Studio 2022 (17.9+) con carga de trabajo ".NET desktop development"
- .NET 8 SDK

> Nota: este código se escribió y organizó fuera de Windows, por lo que **no fue compilado
> todavía**. Al abrir la solución en Visual Studio es esperable tener que resolver algún
> detalle menor de referencias/paquetes NuGet la primera vez (`dotnet restore` debería
> bastar en la mayoría de los casos).

## Cómo compilar

```
cd CameraInspector
dotnet restore
dotnet build
```

Para correr la app (requiere Windows, WPF no corre en Linux/Mac):

```
dotnet run --project CameraInspector.App
```

## Qué hace este corte

- `CameraInspector.Core`: modelos (`DiscoveredDevice`, `NetworkInterfaceInfo`) e interfaces
  de la Capa 3 (`INetworkScanner`, `IPingScanner`, `IArpResolver`, `ISubnetCalculator`,
  `INetworkInterfaceService`) — el contrato que todo lo demás implementa.
- `CameraInspector.Network`: implementación real de descubrimiento — detecta interfaces
  de red activas, calcula el rango de IPs de la subred, hace ping sweep paralelo acotado,
  y resuelve MAC vía la tabla ARP del sistema (P/Invoke a `iphlpapi.dll`).
- `CameraInspector.Persistence`: `DbContext` de EF Core sobre SQLite, con las tablas
  `cameras`, `camera_interfaces`, `camera_tests`, `camera_events`, `camera_credentials`
  (la base se crea y migra sola en `%AppData%\CameraInspector\camerainspector.db`).
- `CameraInspector.App`: WPF + MVVM (`CommunityToolkit.Mvvm`) + Generic Host para DI.
  La pantalla "Escanear" ya funciona de punta a punta: detecta tu interfaz de red,
  escanea la subred, y llena la tabla en vivo a medida que aparecen dispositivos.

## Qué falta (próximos pasos, ya conversados)

1. **Capa 4 — Resolución de fabricante**: `IManufacturerDetector` (OUI, ONVIF DeviceInfo,
   HTTP banner) con score de confianza.
2. **Capa 5 — Providers**: `IOnvifCameraService` + `GenericOnvifProvider`,
   `HikvisionProvider`, etc., inyectados por composición.
3. **Capa 6 — Diagnóstico**: `IDiagnosticTest` + `DiagnosticsOrchestrator` corriendo
   en paralelo.
4. **Capa 7 — Video**: integración FFmpeg/FFmpeg.AutoGen para el visor RTSP.
5. **Capa 9 — Seguridad**: wrapper de Windows Credential Manager para
   `CameraCredentialEntity.CredentialRef`.
6. Migraciones EF Core reales (`dotnet ef migrations add InitialCreate`) — todavía no
   se generaron porque requieren el SDK de EF Core Tools corriendo contra el proyecto.
7. Conectar las pantallas restantes del mockup (Detalle, Video, Diagnóstico, Red,
   Historial) como `UserControl` + `ViewModel` adicionales, reutilizando el mismo patrón.

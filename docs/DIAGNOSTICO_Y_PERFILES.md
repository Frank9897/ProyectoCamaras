# Diagnóstico, ficha técnica y perfiles de escaneo

## Diagnóstico de red del PC

La herramienta puede inspeccionar la interfaz IPv4 seleccionada y mostrar:

- nombre y descripción del adaptador;
- estado del enlace;
- MAC;
- IP del PC;
- máscara y prefijo CIDR;
- red objetivo calculada;
- gateway;
- DNS;
- DHCP o IP fija;
- tipo de interfaz.

También permite probar por ICMP el gateway y los DNS informados.

La red objetivo no es la misma cosa que la IP del PC. Por ejemplo, con `192.168.0.106/24`, el PC tiene la IP `192.168.0.106` y pertenece a la red `192.168.0.0/24`.

## Perfiles de escaneo

- **Directa:** una IP concreta o un enlace APIPA. No realiza un barrido completo.
- **Red local:** utiliza la subred calculada desde la interfaz seleccionada.
- **Red completa:** recorre las subredes de las interfaces IPv4 activas elegibles.

Los perfiles son accesibles desde las herramientas de la tabla de resultados y reutilizan los comandos de escaneo existentes.

## Ficha técnica

La ficha unifica identidad, salud operacional, comunicación, video, ONVIF y evidencia de detección de una cámara.

Incluye los endpoints ONVIF conocidos y la evidencia acumulada que llevó a clasificar el dispositivo como cámara.

La ficha no convierte por sí sola a un host de red en cámara: depende de la clasificación y evidencias generadas por el motor de descubrimiento.

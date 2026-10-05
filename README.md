# Comparador de Archivos - VMSofts

Aplicación de escritorio moderna y de alto rendimiento para la comparación y sincronización segura de carpetas y archivos en Windows. Construida en **.NET 10**, **WPF** y **WPF-UI** (estilo Fluent de Windows 11, como PowerToys), diseñada para procesar cientos de miles de archivos sin congelar la interfaz y con total integridad de datos.

---

## Características Principales

- **Arquitectura Asíncrona sin Bloqueos:** El escaneo y la comparación informan el progreso mediante contadores desacoplados de la interfaz (5 lecturas/segundo), permitiendo comparar más de 100.000 archivos en segundos manteniendo la ventana fluida y receptiva.
- **Cualquier origen y destino:** disco local, memorias USB, unidades de red mapeadas, rutas `\\servidor\carpeta` y **teléfonos y cámaras por USB (MTP)**, con el botón *Dispositivos* que lista lo conectado. Un teléfono se escribe como `mtp:\\Nombre del teléfono\Almacenamiento interno\DCIM`.
- **Flujo en pasos que se pueden volver a abrir:** el menú lateral (Ubicaciones, Comparación, Copia, Configuración) deja regresar a cualquier paso ya hecho, y navegar nunca cancela una copia en curso.
  1. **Ubicaciones:** origen y destino (escribir, examinar, dispositivos o arrastrar una carpeta), varios pares en una pasada, recientes y exclusiones.
  2. **Comparación:** progreso en vivo y luego la tabla virtualizada con filtros, búsqueda, acciones por archivo y exportación a CSV (UTF-8 BOM para Excel).
  3. **Copia:** cada archivo en curso con su propia barra, velocidad, tiempo restante, cuántos hilos se usan y por qué, los últimos terminados y todos los que fallaron con su motivo.
- **Hilos de copia configurables:** automático según el dispositivo (2 en USB, 4 en disco y red, 1 con teléfonos) o un número fijo de 1 a 16.
- **Modos de Comparación:**
  - **Rápido:** Evaluación instantánea por tamaño y fecha de modificación con tolerancia de 2 segundos (ideal para discos externos y sistemas de archivos con distinta precisión temporal).
  - **Exacto:** Comprobación criptográfica SHA-256 en paralelo multi-hilo.
- **Copia Segura Atómica:**
  - Escritura inicial en archivo temporal (`.comparador-tmp`) junto al destino.
  - El SHA-256 del origen se calcula en la misma lectura de la copia (no se lee dos veces) y, si se pide verificar, se relee la copia y se comparan las huellas antes del reemplazo final. Si no coinciden, el destino no se toca.
  - Reemplazo atómico preservando fechas de creación, modificación y atributos originales.
  - Sin riesgo de archivos corruptos ante cortes de energía o desconexiones.
- **Detección Real de Archivos en Uso:**
  - Utiliza la API nativa **Windows Restart Manager** para identificar el nombre exacto de la aplicación y proceso que bloquea un archivo.
- **Soporte de Rutas Largas:** Compatible con rutas de más de 260 caracteres mediante prefijos de Windows extendidos (`\\?\`).
- **Prevención de Suspensión:** Evita que el equipo entre en estado de suspensión o reposo durante operaciones prolongadas (`SetThreadExecutionState`).
- **Tema Visual:** claro, oscuro o el de Windows (lo sigue si cambia), con fondo Mica.

---

## Estructura de la Solución

- **`src/Comparador.Nucleo`** (.NET 10.0): Biblioteca de clases con la lógica de negocio pura, la capa `Ubicaciones` (disco/USB/red y teléfono MTP con MediaDevices), modelos, servicios de comparación, escaneo, hashing SHA-256, detector de bloqueos de Windows y sincronización atómica.
- **`src/Comparador.App`** (.NET 10.0-windows): Aplicación WPF con WPF-UI (Fluent), patrón MVVM (CommunityToolkit.Mvvm), virtualización de UI y diálogos nativos.
- **`tests/Comparador.Pruebas`** (.NET 10.0-windows): Batería completa de pruebas unitarias y de rendimiento con xUnit (clasificación, tolerancia de fechas, SHA-256, exclusiones, archivos en uso, rutas largas de >260 caracteres, copia corrupta detectada sin tocar el destino, hilos por dispositivo y rendimiento con 20.000 y 100.000 archivos).

---

## Requisitos y Compilación

- **SDK de .NET 10.0** o superior.
- **Windows 10 / 11** (x64 / ARM64).

### Compilar el proyecto

```bash
dotnet build src/Comparador.App -c Release
```

### Ejecutar las pruebas

```bash
dotnet test tests/Comparador.Pruebas
```

---

## Créditos y Autoría

- **Desarrollo y Arquitectura:** ING Victor Maldonado / **VMSofts**
- **Licencia:** MIT (consulte el archivo [LICENSE](LICENSE) para más detalles).

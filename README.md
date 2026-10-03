# Comparador de Archivos - VMSofts

Aplicación de escritorio moderna y de alto rendimiento para la comparación y sincronización segura de carpetas y archivos en Windows. Construida en **.NET 10**, **WPF** y **Material Design**, diseñada para procesar cientos de miles de archivos sin congelar la interfaz y con total integridad de datos.

---

## Características Principales

- **Arquitectura Asíncrona sin Bloqueos:** El escaneo y la comparación informan el progreso mediante contadores desacoplados de la interfaz (5 lecturas/segundo), permitiendo comparar más de 100.000 archivos en segundos manteniendo la ventana fluida y receptiva.
- **Flujo Guiado en 4 Pasos:**
  1. **Carpetas:** Selección intuitiva de origen y destino con arrastrar y soltar (Drag and Drop), historial de pares recientes y personalización de exclusiones.
  2. **Comparar:** Lectura e indexación en memoria con cálculo de velocidad y tiempo estimado en tiempo real.
  3. **Revisión:** Tabla virtualizada con filtrado instantáneo (Pendientes, Faltan, Diferentes, Sobran, Problemas, Iguales), búsqueda en vivo, menú de acciones individuales y exportación a CSV con codificación UTF-8 BOM para Excel.
  4. **Sincronizar:** Copia atómica segura con resumen final de elementos copiados y fallidos.
- **Modos de Comparación:**
  - **Rápido:** Evaluación instantánea por tamaño y fecha de modificación con tolerancia de 2 segundos (ideal para discos externos y sistemas de archivos con distinta precisión temporal).
  - **Exacto:** Comprobación criptográfica SHA-256 en paralelo multi-hilo.
- **Copia Segura Atómica:**
  - Escritura inicial en archivo temporal (`.comparador-tmp`) junto al destino.
  - Verificación opcional SHA-256 antes del reemplazo final.
  - Reemplazo atómico preservando fechas de creación, modificación y atributos originales.
  - Sin riesgo de archivos corruptos ante cortes de energía o desconexiones.
- **Detección Real de Archivos en Uso:**
  - Utiliza la API nativa **Windows Restart Manager** para identificar el nombre exacto de la aplicación y proceso que bloquea un archivo.
- **Soporte de Rutas Largas:** Compatible con rutas de más de 260 caracteres mediante prefijos de Windows extendidos (`\\?\`).
- **Prevención de Suspensión:** Evita que el equipo entre en estado de suspensión o reposo durante operaciones prolongadas (`SetThreadExecutionState`).
- **Tema Visual:** Soporte completo para temas Claro y Oscuro con paleta Material Design.

---

## Estructura de la Solución

- **`src/Comparador.Nucleo`** (.NET 10.0): Biblioteca de clases con la lógica de negocio pura, modelos, servicios de comparación, escaneo, hashing SHA-256, detector de bloqueos de Windows y sincronización atómica.
- **`src/Comparador.App`** (.NET 10.0-windows): Aplicación WPF con Material Design In XAML Toolkit, patrón MVVM (CommunityToolkit.Mvvm), virtualización de UI y diálogos nativos.
- **`tests/Comparador.Pruebas`** (.NET 10.0-windows): Batería completa de pruebas unitarias y de rendimiento con xUnit (clasificación, tolerancia de fechas, SHA-256, exclusiones, archivos en uso, rutas largas de >260 caracteres y rendimiento con 20.000 y 100.000 archivos).

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

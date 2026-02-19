Referencia de API del Núcleo
============================

Documentación técnica de las clases críticas del espacio de nombres ``DbfForge.Core``.

Readers Namespace
-----------------

Clase ``ExcelReaderNPOI``
~~~~~~~~~~~~~~~~~~~~~~~~~
Implementa ``IDisposable``. Wrapper sobre la librería NPOI para acceso a archivos XLS/XLSX.

**Propiedades:**

* ``FieldCount`` (int): Número máximo de columnas detectadas en la hoja actual.
* ``SheetName`` (string): Nombre de la pestaña activa.

**Métodos Públicos:**

* ``bool MoveToNextSheet()``: Avanza el cursor a la siguiente hoja del libro. Reinicia el enumerador de filas.
* ``bool ReadRow()``: Avanza el iterador a la siguiente fila física. Devuelve ``false`` al final.
* ``object GetValue(int index)``:
    * Recupera el valor de la celda en el índice dado.
    * **Manejo de Errores:** Captura excepciones de celda corrupta y devuelve cadena vacía.
    * **Fórmulas:** Evalúa ``CachedFormulaResultType`` para devolver el valor pre-calculado.

Clase ``OdsReader``
~~~~~~~~~~~~~~~~~~~
Implementa ``IDisposable``. Lector de alto rendimiento para formato OpenDocument.

**Detalles de Implementación:**
Utiliza ``System.IO.Compression.ZipArchive`` para acceder al stream ``content.xml``.

* **SpanBuffer:** ``Dictionary<int, (object, int)>``. Buffer interno para gestionar celdas que ocupan múltiples filas (`row-span`).
* **Limitaciones:** Acceso unidireccional (Forward-only). No soporta saltos aleatorios a filas anteriores.

UI Namespace
------------

Clase ``MainMenu`` (Controlador)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Hereda de ``Godot.Control``.
Gestiona la inyección de dependencias, el ciclo de vida de la aplicación y la cola de procesamiento.

**Estructuras Anidadas:**

* ``struct DetectedColumn``: Define la meta-información de una columna (Nombre, Tipo, Longitud).
* ``class ForgeTicket``: Objeto que encapsula todo el trabajo necesario para exportar un archivo (Source -> Destination + Schema).

Clase ``SheetCard`` (Componente)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Hereda de ``Godot.PanelContainer``.
Representa visualmente un archivo en la cola de procesamiento.

**Eventos:**

* ``OnManualFormatChange``: Disparado cuando el usuario cambia el formato de salida en el dropdown, usado para sincronización con el control maestro.
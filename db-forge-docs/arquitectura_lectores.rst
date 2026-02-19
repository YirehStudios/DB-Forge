Arquitectura de Motores de Lectura
==================================

BD Forge utiliza una estrategia de "fábrica de lectores" para instanciar el motor más adecuado según la extensión del archivo.

1. Lector Excel NPOI (DOM / Heap-Intensive)
-------------------------------------------

Ubicación: ``DbfForge.Core.Readers.ExcelReaderNPOI.cs``

Este lector está diseñado para la complejidad de Microsoft Excel. Carga todo el archivo en memoria (DOM) para permitir el acceso aleatorio necesario para resolver fórmulas y referencias.

Algoritmo de Desagrupación (The Unmerge Logic)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Excel guarda las celdas combinadas como metadatos de "Regiones", dejando las celdas hijas vacías físicamente. BD Forge reconstruye la integridad tabular:

1.  **Mapeo (Pre-Scan):** Al abrir la hoja, se itera sobre ``sheet.MergedRegions``.
2.  **Indexación:** Se construye un ``Dictionary<string, ICell> _mergedCellsMap``.
    * *Clave:* Coordenada "Fila_Columna" de cada celda hija.
    * *Valor:* Referencia al objeto ``ICell`` de la celda padre (Top-Left).
3.  **Intercepción:** En el método ``GetValue(int index)``, se consulta el diccionario. Si la coordenada existe, se desvía la lectura al padre.

.. code-block:: csharp

   // Lógica simplificada del mapeo
   string mergeKey = $"{rowIndex}_{index}";
   if (_mergedCellsMap.ContainsKey(mergeKey)) 
       cell = _mergedCellsMap[mergeKey]; // Redirección de puntero

Recuperación de Tiempos Negativos
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Excel muestra tiempos negativos (ej. "-10:00") como ``#####``.
* **Solución Técnica:** El lector accede a la propiedad ``NumericCellValue`` cruda (que es un `double` representando días, ej. `-0.41`) y reconstruye matemáticamente el string de tiempo usando ``TimeSpan.FromDays()``, ignorando la máscara visual de Excel.

2. Lector OpenDocument (Streaming / Low-Memory)
-----------------------------------------------

Ubicación: ``DbfForge.Core.Readers.OdsReader.cs``

Diseñado para *Big Data* en escritorio. Un archivo ODS es un ZIP que contiene un XML (``content.xml``).

Estrategia de Flujo (Forward-Only)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
En lugar de descomprimir todo el XML en RAM (lo cual colapsaría con archivos grandes), utilizamos ``System.Xml.XmlReader``.
* **Ventaja:** Consumo de RAM O(1). No importa si el archivo tiene 1,000 o 1,000,000 de filas, la memoria usada es constante (~50-100MB).
* **Desventaja:** No se puede volver atrás. La inferencia de tipos debe hacerse en una sola pasada o requiere reabrir el flujo.

El Problema de "Number-Columns-Repeated"
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
El formato ODS comprime celdas repetidas. Una fila con 1000 celdas vacías se guarda como un solo nodo XML: ``<table:table-cell number-columns-repeated="1000" />``.

**El Buffer de Expansión (`_spanBuffer`):**
El lector implementa una máquina de estados que "inyecta" celdas virtuales.

1.  Al leer un nodo con repetición, se calcula el contador.
2.  Un bucle ``for`` emite el valor N veces hacia el array de la fila actual (`_currentRow`).
3.  **Manejo de Spans Verticales:** Si una celda tiene ``number-rows-spanned``, se guarda en un ``Dictionary<int, (object, int)>`` (Buffer de Spans).
4.  En las filas subsiguientes, antes de leer el XML, el lector consulta el Buffer e inyecta los valores "fantasma" que vienen de filas superiores, manteniendo la alineación perfecta de la tabla.
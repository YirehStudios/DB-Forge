Arquitectura del Sistema y Núcleo de Procesamiento
==================================================

.. note::
   Esta sección asume conocimiento técnico de C# y estructuras de datos.

El núcleo de BD Forge opera sobre una arquitectura modular desacoplada, donde la interfaz de usuario (`MainMenu.cs`) actúa como un orquestador que inyecta dependencias de lectura (`IReader` implícito) dependiendo de la extensión del archivo.

1. Estrategias de Ingesta (The Readers)
---------------------------------------

El sistema implementa dos estrategias de lectura diametralmente opuestas para optimizar el balance entre funcionalidad y rendimiento.

A. Lector Excel DOM (``ExcelReaderNPOI.cs``)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Para archivos de Microsoft Excel (`.xlsx`, `.xls`), se utiliza **NPOI** para cargar el Modelo de Objetos del Documento (DOM) completo en la memoria gestionada (Heap).

* **Algoritmo de Desagrupación Lógica (Unmerge):**
  Excel almacena las celdas combinadas como regiones de coordenadas, donde solo la celda superior izquierda contiene el valor.
  
  .. code-block:: csharp
  
     // Mapa de Complejidad O(1) para búsquedas rápidas durante la lectura
     private Dictionary<string, ICell> _mergedCellsMap;
     
     // Durante la inicialización, pre-calculamos todas las redirecciones:
     _mergedCellsMap[$"{r}_{c}"] = parentCell;

  Al leer una celda vacía, el lector consulta este mapa. Si la coordenada existe, redirige el puntero a la celda "padre", garantizando integridad referencial absoluta.

* **Recuperación de Tiempos Negativos:**
  Excel visualiza tiempos negativos como ``#####``. BD Forge accede al valor flotante subyacente (ej. ``-0.0416``) y lo reconstruye matemáticamente a ``-01:00:00`` usando ``TimeSpan.FromDays()``.

B. Lector OpenDocument Streaming (``OdsReader.cs``)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Para archivos `.ods`, la carga del DOM es inviable debido a la ineficiencia del XML de OpenOffice. Se implementa un lector de flujo unidireccional (*Forward-Only*).

* **Arquitectura de Streaming:**
  Utiliza ``XmlReader`` sobre el archivo ``content.xml`` extraído al vuelo del contenedor ZIP. Esto mantiene la huella de memoria (RAM Footprint) extremadamente baja, independientemente del tamaño del archivo.

* **Manejo de Spans y Repeticiones:**
  ODS comprime filas repetidas con atributos como ``number-columns-repeated="1024"``.
  
  * **Problema:** Un lector XML ingenuo leería solo 1 nodo, desalineando la tabla.
  * **Solución (The Span Buffer):**
    El lector mantiene un ``Dictionary<int, (object Value, int RemainingRows)> _spanBuffer``.
    Si una celda tiene un ``row-span``, su valor se almacena en el buffer. En las siguientes iteraciones de ``ReadRow()``, el lector inyecta artificialmente estos valores buffered en la posición correcta antes de leer el XML real de esa fila.

2. El Motor de Exportación ("The Hopper")
-----------------------------------------

El proceso de exportación se encapsula en la clase ``ForgeTicket`` (DTO).

* **Saneamiento (SanitizeDataWithReport):**
  Antes de escribir en disco, cada dato pasa por una función pura que normaliza el valor según el tipo de destino.
  
  * *Detección de Ambigüedad Numérica:* Resuelve el conflicto de separadores (punto vs coma) analizando la posición del último índice de cada caracter. Si ``LastComma > LastDot``, asume formato Europeo y normaliza.
  
* **Truncamiento Seguro (Overflow Protection):**
  En el driver ``FastDBF``, se habilita explícitamente ``AllowIntegerTruncate = true``.
  * *Justificación:* En procesos por lotes de 1 millón de registros, es preferible guardar un registro con asteriscos (``****``) que detener todo el proceso por una excepción de desbordamiento (`System.OverflowException`).

3. Modelo de Datos y DTOs
-------------------------

.. list-table:: Objetos de Transferencia de Datos
   :widths: 25 75
   :header-rows: 1

   * - Clase
     - Responsabilidad
   * - ``SheetData``
     - Contiene la matriz de datos crudos (`RawRows`) y la muestra de depuración (`DebugSample`). Reside en memoria RAM.
   * - ``DetectedColumn``
     - Metadatos inferidos: Nombre, Tipo Sugerido (int), Longitud y Decimales.
   * - ``ForgeTicket``
     - El contrato final para el motor de exportación. Contiene los datos saneados y las rutas de destino calculadas.
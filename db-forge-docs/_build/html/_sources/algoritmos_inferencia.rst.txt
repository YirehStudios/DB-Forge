Motor de Inferencia Estadística y Saneamiento
=============================================

BD Forge no confía en los metadatos de formato de los archivos fuente, ya que los usuarios frecuentemente formatean números como texto. El sistema utiliza análisis de contenido.

1. Muestreo Estriado (Stride Sampling)
--------------------------------------

Para inferir tipos en archivos grandes sin leerlos completos, se utiliza un algoritmo de paso variable.

* **Objetivo:** Obtener una muestra representativa de 200 filas distribuidas uniformemente.
* **Cálculo del Paso (Stride):** ``int step = Math.Max(1, TotalRows / 200);``
* **Ejecución:** El lector salta ``step`` filas entre cada lectura. Esto permite detectar datos que solo aparecen al final del archivo (ej. totales o notas) que un lector de "primeras N filas" perdería.

2. Sistema de Votación de Tipos
-------------------------------

Para cada columna, se ejecuta una elección democrática entre los tipos de datos.

.. list-table:: Reglas de Votación
   :widths: 20 80
   :header-rows: 1

   * - Candidato
     - Regla de Detección (Regex / Parsers)
   * - **Time**
     - Regex ``^-?\d+:\d{2}``. Prioridad alta para no confundir "12:00" con texto.
   * - **Date**
     - Busca separadores `/` o `-` y valida con ``DateTime.TryParse``.
   * - **Numeric**
     - ``double.TryParse``. Analiza presencia de punto/coma para determinar decimales.
   * - **Logical**
     - Diccionario cerrado: {T, F, Y, N, 1, 0, YES, NO, SI}.

**El Umbral de Victoria:**
Un tipo de dato gana solo si representa más del **50%** de los valores no nulos de la muestra. Si hay empate o ambigüedad, el sistema recurre al tipo seguro: **Character**.

3. Normalización Heurística de Números
--------------------------------------

El método ``SanitizeDataWithReport`` (en `MainMenu.cs`) maneja el problema de la localización (Punto vs Coma decimal).

**El Algoritmo de "Último Separador":**
Cuando un número viene como texto (ej. "1,200.50"), el sistema busca los índices de `.` y `,`.

.. code-block:: csharp

   if (lastComma > lastDot) {
       // Formato Europeo detectado (1.200,50)
       // Acción: Eliminar puntos, reemplazar coma por punto estándar.
   } else {
       // Formato Americano detectado (1,200.50)
       // Acción: Eliminar comas.
   }

Esto permite que BD Forge procese correctamente archivos con formatos numéricos mixtos o de diferentes regiones sin configuración manual del usuario.
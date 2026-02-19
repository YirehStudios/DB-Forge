Motor de Inferencia Estadística (Stride Sampling)
=================================================

BD Forge no confía en los metadatos de Excel, ya que los usuarios suelen formatear celdas numéricas como texto. En su lugar, utiliza un algoritmo de inferencia basado en contenido.

Algoritmo de Muestreo Estriado
------------------------------

En lugar de leer las primeras N filas (que podrían ser encabezados vacíos), el sistema implementa un muestreo distribuido uniformemente.

* **Población:** Total de filas del documento.
* **Tamaño de Muestra:** 200 registros.
* **Stride (Paso):** ``Math.Max(1, TotalRows / 200)``.

El sistema itera la tabla completa saltando cada ``Stride`` filas. Esto asegura que la muestra sea representativa tanto del inicio, medio y final del archivo.

Sistema de Votación
-------------------

Para cada columna, se ejecuta un análisis de patrones mediante Expresiones Regulares (`Regex`) y parsing de prueba.

.. code-block:: csharp

   // Pseudocódigo del sistema de votación
   foreach (celda in muestra) {
       if (EsTiempo(celda)) vTime++;
       else if (EsFecha(celda)) vDate++;
       else if (EsNumero(celda)) vNum++;
       else if (EsBooleano(celda)) vBool++;
   }

**Umbral de Decisión:**
El tipo ganador debe superar el **50% de validez** (`validCount * 0.5`).

Jerarquía de Evaluación
~~~~~~~~~~~~~~~~~~~~~~~

El orden de comprobación es estricto para evitar falsos positivos:

1.  **Time (5):** Regex estricto ``^-?\d+:\d{2}``. Prioritario porque "12:00" puede parecer numérico o texto.
2.  **Date (3):** Debe contener separadores (`/` o `-`) y ser parseable por `DateTime`.
3.  **Numeric (1/2):** `double.TryParse`. Detecta si tiene punto decimal para diferenciar `Integer` de `Numeric`.
4.  **Logical (4):** Conjunto cerrado {T, F, Y, N, 1, 0, YES, NO}.
5.  **Character (0):** Fallback por defecto (Default).

Detección de Columnas Fantasma
------------------------------

Si una columna tiene encabezado (ej. "FIELD_15") pero el conteo de datos válidos en la muestra es **cero** (solo nulos o espacios en blanco), se marca como ``IsGhostColumn = true``.

* **Efecto:** La columna se oculta automáticamente en la interfaz (`SheetCard`) y se excluye de la exportación por defecto, limpiando la estructura final.
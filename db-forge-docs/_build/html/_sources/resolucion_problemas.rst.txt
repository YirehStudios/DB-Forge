Guía de Resolución de Problemas (Troubleshooting)
=================================================

Diagnóstico y solución de errores comunes reportados por el motor de BD Forge.

Errores de Archivo y Sistema
----------------------------

Error: "El proceso no puede acceder al archivo porque está siendo utilizado"
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
**Código de Error:** `System.IO.IOException` (Sharing violation).

* **Síntoma:** El indicador de estado se pone rojo (ERROR) inmediatamente al intentar procesar.
* **Causa Técnica:** BD Forge intenta abrir el archivo con `FileShare.ReadWrite`. Sin embargo, si Excel está editando una celda activamente (cursor parpadeando), bloquea el archivo de manera exclusiva.
* **Solución:**
    1. Guarde los cambios en Excel.
    2. Salga del modo de edición de celda (presione ESC o Enter).
    3. No es necesario cerrar Excel, solo asegurar que no haya celdas activas.

Error: "Memory Limit Exceeded" o Cierre Inesperado
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
* **Síntoma:** La aplicación se cierra sin mensaje al cargar un archivo Excel muy grande (>50MB).
* **Causa:** El lector NPOI carga todo el DOM en memoria. Un archivo de 50MB en disco puede expandirse a 2GB de objetos en RAM.
* **Solución:**
    * Guarde el archivo como **.ODS (OpenDocument Spreadsheet)** en Excel o LibreOffice.
    * BD Forge procesará el archivo ODS usando el modo *Streaming*, consumiendo menos de 500MB de RAM incluso para millones de filas.

Anomalías de Datos
------------------

Columnas "Fantasmas" o vacías
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
* **Síntoma:** Aparecen columnas llamadas "F12", "F13" sin datos.
* **Causa:** Excel a menudo mantiene referencias a celdas que fueron borradas pero cuyo formato persiste.
* **Comportamiento de BD Forge:** El sistema detecta estas columnas durante el muestreo. Si la columna está vacía en la muestra de 200 filas, se marca automáticamente como deshabilitada (Ghost Column). Puede reactivarla manualmente en la configuración si sabe que hay datos más adelante.

Fechas con formato incorrecto (Día/Mes invertido)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
* **Causa:** El archivo de texto/CSV tiene fechas en formato `dd/mm/yyyy` pero el servidor donde corre BD Forge está configurado en Inglés (`mm/dd/yyyy`).
* **Solución:**
    1. Cambie el tipo de dato de la columna a **Character** en la ficha de configuración.
    2. Exporte el archivo.
    3. Realice la conversión de fecha en su motor de base de datos destino (SQL) usando una función determinista (ej. `CONVERT(date, campo, 103)`).
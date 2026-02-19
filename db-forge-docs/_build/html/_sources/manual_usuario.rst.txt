Manual del Operador
===================

Este manual detalla la operación de la interfaz gráfica de usuario (GUI) de BD Forge vE 2.0.0.0. El sistema está diseñado para minimizar el error humano mediante retroalimentación visual inmediata.

1. La Interfaz "Data Hopper"
----------------------------

La ventana principal actúa como una tolva de procesamiento centralizado.

Zona de Arrastre (Drop Zone)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~
El panel central ("EmptyStatePanel") detecta eventos de arrastre del sistema operativo.
* **Archivos Admitidos:** `.xlsx`, `.xls`, `.ods`, `.csv`, `.txt`, `.dbf`.
* **Comportamiento:** Al soltar archivos, el sistema instancia una ``SheetCard`` por cada documento válido. Los archivos no reconocidos son registrados en el log como advertencias (`WARN`) y descartados silenciosamente para no interrumpir el flujo por lotes.

Barra de Herramientas Global
~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Ubicada en la parte inferior (`WorkspaceFooter`), controla el destino de la exportación masiva.

* **Format Dropdown:** Permite forzar un formato de salida para **todas** las fichas activas.
    * *[ Individual ]*: Respeta la configuración de cada tarjeta.
    * *DBF (dBase)*: Fuerza compatibilidad VFP9.
    * *Excel (.xlsx)*: Genera reportes nativos.
    * *CSV (Text)*: Texto plano delimitado por comas.
* **Merge Switch (Interruptor de Fusión):**
    * **OFF:** Cada archivo de entrada genera un archivo de salida independiente.
    * **ON:** Activa el algoritmo de consolidación. Todas las fichas se fusionan en un solo archivo maestro (definido en `MergeFilenameInput`).

2. Fichas de Control (Sheet Cards)
----------------------------------

Cada archivo cargado se representa como un objeto visual complejo (`SheetCard.cs`).

Semáforo de Estado (Visual Helper)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
El sistema comunica la salud del proceso mediante un código de colores y estados, gestionado por la clase ``VisualCardHelper``:

+------------+------------+-----------------------------------------------------------+
| Estado     | Color      | Significado Técnico                                       |
+============+============+===========================================================+
| **READY** | Cian       | Estructura analizada y lista. Sin conflictos detectados.  |
| **RISK** | Naranja    | **Alerta de Truncamiento.** Un dato en la muestra excede  |
|            |            | el largo definido en la columna.                          |
| **PROC** | Azul       | El hilo de exportación está escribiendo en disco.         |
| **OK** | Verde      | Exportación finalizada con integridad 100%.               |
| **WARN** | Amarillo   | Finalizado, pero hubo mutación de datos (redondeos, etc). |
| **ERROR** | Rojo       | Fallo crítico de E/S o bloqueo de archivo.                |
+------------+------------+-----------------------------------------------------------+

Editor de Estructura (Live Debugging)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
Al expandir una ficha ("Configure Fields"), se presenta la matriz de columnas detectadas.

* **Validación en Tiempo Real:** El sistema implementa un *Debounce Timer* de 0.5 segundos. Cuando el usuario modifica un tipo de dato o una longitud, el sistema re-escanea una muestra de 50 registros (`DebugSample`) en memoria.
* **Indicadores de Fila:**
    * **✔ (Verde):** Todos los datos de la muestra caben en la definición actual.
    * **! (Rojo):** Se ha detectado un dato que será truncado. *Ejemplo: Intentar guardar "2023-01-01" (10 chars) en un campo de largo 8.*

3. Tipos de Datos Soportados
----------------------------

El motor normaliza cualquier entrada a uno de los 6 tipos primitivos soportados por el kernel de exportación:

1.  **Character (0):** Texto alfanumérico. Máximo 254 caracteres (Límite estricto DBF).
2.  **Numeric (1):** Números con punto flotante. Soporta notación científica y negativos.
3.  **Integer (2):** Enteros puros (Long). El sistema trunca decimales automáticamente.
4.  **Date (3):** Fechas. El sistema intenta sanear formatos locales (`dd/mm/yy`) e ISO (`yyyy-mm-dd`).
5.  **Logical (4):** Booleano. Reconoce `T/F`, `Y/N`, `1/0`, `Si/No`.
6.  **Time (5):** Tiempo o Duración. Convierte fracciones decimales de Excel (ej: `0.5` -> `12:00:00`).
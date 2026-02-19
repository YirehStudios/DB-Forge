Filosofía y Arquitectura del Sistema
====================================

BD Forge nace de una necesidad crítica en la gestión de bases de datos: **la limpieza de la fuente**. Los usuarios finales suelen tratar a Excel como un lienzo de pintura en lugar de una base de datos, combinando celdas, usando colores como datos y mezclando textos con números.

El Paradigma "Data Hopper"
--------------------------

El concepto de "Tolva" implica un embudo de entrada amplio pero una salida estrecha y controlada.

* **Entrada (Ingesta):** Acepta imperfecciones. Archivos con celdas combinadas, columnas sin nombre, formatos de fecha mixtos (DD/MM vs MM/DD) y basura visual.
* **El Proceso (La Forja):**
    1.  **Aplanamiento:** Desagrupa lógicamente las jerarquías visuales.
    2.  **Inferencia:** Adivina la intención del dato mediante muestreo estadístico, no solo leyendo la cabecera.
    3.  **Saneamiento:** Aplica reglas de mutación para forzar tipos estrictos.
* **Salida (Egreso):** Produce archivos DBF/SQL relacionalmente puros. Si un dato no cabe, se trunca o se rechaza, pero **nunca** se rompe la estructura de la tabla.

Arquitectura de Alto Nivel (C4 Model - Nivel 2)
-----------------------------------------------

El sistema opera como una aplicación monolítica de escritorio construida sobre **Godot Engine 4.x** con un módulo .NET 6.0 integrado.

.. code-block:: text

   [ USUARIO ] 
        | (Drag & Drop)
        v
   [ CAPA DE PRESENTACIÓN (GODOT UI) ]
   |  - MainMenu.cs (Orquestador)
   |  - SheetCard.cs (Estado Visual)
   |  - VisualCardHelper (Animaciones)
   |
   +---> [ CAPA DE NEGOCIO (.NET CORE) ]
            |
            +-- [ MOTOR DE LECTURA (Readers) ]
            |      |-- ExcelReaderNPOI (DOM / Memoria Alta)
            |      |-- OdsReader (Streaming / Memoria Baja)
            |      +-- CsvReader (Texto Plano)
            |
            +-- [ MOTOR DE INFERENCIA ]
            |      +-- Algoritmo de Muestreo Estriado (Stride Sampling)
            |
            +-- [ MOTOR DE EXPORTACIÓN (The Hopper) ]
                   |-- FastDBF Driver (Salida xBase)
                   |-- NPOI Writer (Salida Excel)
                   +-- CSV Writer (Salida RFC 4180)

Principios de Diseño
--------------------

1.  **Integridad sobre Comodidad:** El sistema prefiere abortar un proceso o llenar un campo con ``****`` (error de desbordamiento) antes que permitir que un número ``12345`` se guarde como ``1234`` por falta de espacio silenciosa.
2.  **Memoria Híbrida:** Reconoce que no todos los archivos son iguales. Usa carga completa (DOM) para archivos pequeños/complejos (Excel) y flujo continuo (Streaming) para archivos masivos (ODS).
3.  **Auditoría Forense:** Cada mutación de dato (ej. cambiar "12.5" a 12 en un campo entero) debe dejar un rastro auditable.
=======================================
BD Forge (Data Hopper) - vE 2.0.0.0
=======================================

.. image:: https://img.shields.io/badge/License-GPLv3-blue.svg
   :target: LICENSE
.. image:: https://img.shields.io/badge/Godot-4.x-478cbf.svg
   :target: https://godotengine.org
.. image:: https://img.shields.io/badge/Estado-Producción-green.svg

**BD Forge** es una solución de ingeniería de datos tipo ETL (Extract, Transform, Load) diseñada para normalizar y "forjar" datos crudos provenientes de fuentes humanas (Excel, CSV) en estructuras relacionales estrictas (DBF, SQL).

A diferencia de un conversor tradicional, BD Forge prioriza la **Integridad Relacional Forzada**: prefiere rechazar un dato ambiguo antes que corromper la base de datos de destino.

Características Principales
---------------------------

* **Motor Híbrido:**
    * Lectura DOM completa para Excel (.xlsx) con soporte de fórmulas.
    * Streaming XML para OpenDocument (.ods) de alto rendimiento.
* **Normalización Inteligente:**
    * Inferencia de tipos estadística (Stride Sampling).
    * Detección de columnas fantasma.
    * Aplanamiento lógico de celdas combinadas (*Unmerge*).
* **Sistema Forense:** Logs detallados de mutación de datos y errores.
* **Interfaz Moderna:** Depuración en vivo (*Live Debugging*) y Temas Dinámicos.

Instalación y Ejecución
-----------------------

Para Usuario Final
~~~~~~~~~~~~~~~~~~

1.  Descargue la última versión desde la sección `Releases`.
2.  Descomprima el archivo ZIP.
3.  Ejecute ``DbfForge.exe``.

Para Desarrolladores (Ejecución Local)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Este proyecto está construido sobre **Godot Engine 4.x (.NET version)**.

1.  **Requisitos:**
    * Godot Engine 4.x (Versión .NET/Mono obligatoria).
    * .NET SDK 6.0 o superior.
    * IDE recomendado: Visual Studio 2022 o VS Code con extensión C#.

2.  **Pasos:**
    * Clone este repositorio.
    * Abra el archivo ``project.godot`` con el editor de Godot.
    * Godot intentará compilar la solución C# automáticamente.
    * Presione **F5** para iniciar el entorno de depuración.

Documentación
-------------

La documentación técnica completa se encuentra en el directorio ``docs/``. Para compilarla y verla en formato web localmente:

.. code-block:: bash

   cd docs
   ./make.bat html

Licencia
--------

Copyright (C) 2026 YirehStudios.
Licenciado bajo **GPL v3**. Ver archivo ``LICENSE`` para más detalles.
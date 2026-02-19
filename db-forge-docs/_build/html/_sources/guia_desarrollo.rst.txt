Guía de Contribución y Entorno de Desarrollo
============================================

Instrucciones para configurar el entorno de desarrollo para BD Forge vE 2.0.0.0.

Requisitos del Sistema
----------------------

* **Godot Engine 4.x (.NET Edition):** Es crucial descargar la versión que soporta C#. La versión estándar GDScript no funcionará.
* **.NET SDK 6.0 o superior:** Requerido para compilar la solución y gestionar paquetes NuGet.
* **IDE Recomendado:** Visual Studio 2022 o JetBrains Rider. VS Code es soportado con la extensión C# Dev Kit.

Estructura del Proyecto
-----------------------

El proyecto sigue una arquitectura híbrida Godot/C#:

* ``/Escenas``: Archivos ``.tscn`` (Interfaz y Prefabs).
* ``/Script``: Código fuente C#.
    * ``/Core/Readers``: Lógica pura de lectura (NPOI/ODS). Independiente de Godot.
    * ``/UI``: Controladores de interfaz (MainMenu, SheetCard).
    * ``/UI/Components``: Componentes visuales reutilizables.
* ``/docs``: Documentación Sphinx (este sitio).

Compilación de Documentación
----------------------------

Esta documentación se genera usando **Sphinx** y el tema **Read the Docs**.

1.  Instalar dependencias (Python requerido):
    
    .. code-block:: bash

       pip install sphinx sphinx-rtd-theme

2.  Generar HTML:
    
    .. code-block:: bash

       cd docs
       .\make.bat html

3.  El resultado estará en ``docs/_build/html/index.html``.
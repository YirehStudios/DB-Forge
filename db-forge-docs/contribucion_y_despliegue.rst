Contribución y Despliegue
=========================

Guía para desarrolladores que deseen compilar el proyecto o generar esta documentación localmente.

Entorno de Desarrollo (Godot)
-----------------------------

Requisitos
~~~~~~~~~~

* **Godot Engine 4.x (.NET Version):** Asegúrese de descargar la versión con soporte C#.
* **.NET SDK 6.0+:** Necesario para compilar la solución ``.sln``.

Ejecución Local
~~~~~~~~~~~~~~~

1.  Clone el repositorio.
2.  Abra el archivo ``project.godot`` con el editor de Godot.
3.  Godot intentará compilar la solución C# automáticamente. Si falla, abra una terminal en la raíz y ejecute:

    .. code-block:: bash

       dotnet build

4.  Presione **F5** en el editor de Godot para iniciar la depuración.

Generación de Documentación (Sphinx)
------------------------------------

Esta documentación utiliza **Sphinx** con el tema **Read the Docs**.

Requisitos Previos
~~~~~~~~~~~~~~~~~~

Necesita Python instalado. Instale las dependencias:

.. code-block:: bash

   pip install sphinx sphinx-rtd-theme

Compilación de HTML
~~~~~~~~~~~~~~~~~~~

1.  Navegue a la carpeta ``docs/``.
2.  Ejecute el script de construcción:

    .. code-block:: bash

       # En Windows (PowerShell/CMD)
       ./make.bat html

       # En Linux/Mac
       make html

3.  Abra el archivo ``docs/_build/html/index.html`` en su navegador web.

Estructura de Directorios
-------------------------

* ``Escenas/``: Archivos ``.tscn`` (UI y Escenas de Godot).
* ``Script/``: Código fuente C# (Lógica de negocio).
    * ``Core/Readers/``: Lógica de bajo nivel (NPOI, ODS).
    * ``UI/Components/``: Lógica de presentación (SheetCard).
* ``docs/``: Archivos fuente de documentación (.rst).
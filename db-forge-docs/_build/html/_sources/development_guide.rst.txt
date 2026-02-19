Contribution & Development Guide
================================

Instructions for setting up the development environment for BD Forge vE 2.0.0.0.

System Requirements
-------------------

* **Godot Engine 4.x (.NET Edition):** It is critical to download the version with C# support (Mono/.NET). The standard GDScript version **will not** compile this project.
* **.NET SDK 6.0+:** Required to build the solution and restore NuGet packages.
* **Recommended IDE:** Visual Studio 2022, JetBrains Rider, or VS Code (with C# Dev Kit).

Project Structure
-----------------

The project follows a hybrid Godot/C# architecture:

* ``/Escenas``: ``.tscn`` files (UI Layouts and Prefabs).
* ``/Script``: C# Source Code.
    * ``/Core/Readers``: Pure I/O logic (NPOI/ODS Readers). Decoupled from Godot APIs.
    * ``/UI``: Interface Controllers (MainMenu, SheetCard).
    * ``/UI/Components``: Reusable visual widgets.
* ``/docs``: Sphinx documentation source (this site).

Building Documentation
----------------------

This documentation is generated using **Sphinx** and the **Read the Docs** theme.

1.  Install dependencies (Python required):
    
    .. code-block:: bash

       pip install sphinx sphinx-rtd-theme sphinx-intl

2.  Build HTML (English Source):
    
    .. code-block:: bash

       cd docs
       .\make.bat html

3.  The output will be located at ``docs/_build/html/index.html``.

Internationalization (i18n)
---------------------------

To generate translations (e.g., Spanish/Portuguese):

1.  Extract translatable messages:
    ``make gettext``
2.  Update message catalogs:
    ``sphinx-intl update -p _build/gettext -l es -l pt``
3.  Edit the generated ``.po`` files in ``docs/locale/``.
4.  Build specific language:
    ``make -e SPHINXOPTS="-D language='es'" html``
@ECHO OFF

pushd %~dp0

REM Command file for Sphinx documentation

REM --- DEFINICION DE VARIABLES CRITICAS ---
REM Esto faltaba en la version anterior y causaba el error de argumentos
set SOURCEDIR=.
set BUILDDIR=_build

REM FORZAR el uso de 'py -m sphinx' directamente.
REM Eliminamos la logica condicional compleja que puede fallar en entornos hibridos.
set SPHINXBUILD=py -m sphinx

REM Verificacion de seguridad basica
%SPHINXBUILD% --version >NUL 2>NUL
if errorlevel 9009 (
	echo.
	echo.The Python module 'sphinx' was not found. 
    echo.Please ensure you have run: py -m pip install sphinx sphinx_rtd_theme
	echo.
	exit /b 1
)

REM Ejecucion directa del comando de construccion
%SPHINXBUILD% -M %1 %SOURCEDIR% %BUILDDIR% %SPHINXOPTS% %O%
goto end

:help
%SPHINXBUILD% -M help %SOURCEDIR% %BUILDDIR% %SPHINXOPTS% %O%

:end
popd
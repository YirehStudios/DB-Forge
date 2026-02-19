Sistema de Logs Forense y Auditoría
===================================

La versión vE 2.0.0.0 introduce un subsistema de logs diseñado para trazabilidad pericial de datos. El objetivo es responder a la pregunta: *"¿Por qué este dato cambió al ser exportado?"*.

Niveles de Registro
-------------------

El sistema clasifica los eventos en cuatro niveles de severidad:

* **[USER] (Cian):** Interacciones de la interfaz (clics, cambios de configuración, drag & drop).
* **[SYS] (Pizarra):** Eventos del ciclo de vida del motor (inicio de drivers, apertura de flujos).
* **[WARN] (Ámbar):** Mutaciones de datos no críticas.
    * *Ejemplo:* Redondeo forzado de decimales o truncamiento de texto que excede el límite.
* **[ERROR] (Rojo):** Excepciones de tiempo de ejecución o fallos de E/S.

Persistencia Dual
-----------------

El logger opera en dos capas simultáneas:

1. **Capa Visual (Memoria UI):**
   Muestra los últimos 300 eventos en el panel lateral derecho del usuario mediante `RichTextLabel` con códigos de color BBCode.

2. **Capa Física (Disco):**
   Escribe inmediatamente (`AutoFlush = true`) en el directorio de usuario:
   ``%APPDATA%\Godot\app_userdata\DBF Forge\logs\``
   
   * **Formato de Archivo:** ``Forge_Session_yyyyMMdd_HHmmss.log``
   * **Bloqueo de Archivos:** El escritor utiliza ``FileShare.Read``, permitiendo que herramientas externas (como `tail` o Notepad++) lean el log en tiempo real mientras BD Forge escribe en él.

Traza Quirúrgica (Surgical Trace)
---------------------------------

Durante la exportación, si se detecta una pérdida de precisión ("Lossy Conversion"), el sistema genera una entrada de traza con coordenadas exactas:

.. code-block:: text

   [WARN] R0045:C012 [DESCRIPCION] | TypeID:1 | IN: '1250.559' (String) >>> OUT: '1250.56' | MUTATION DETECTED

Esto permite a los auditores de datos localizar exactamente qué fila y celda fue modificada por el algoritmo de saneamiento.
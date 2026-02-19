User Interface & Operation Guide
================================

This guide breaks down the visual components of BD Forge ("The Hopper") and how to interpret the real-time feedback system.

1. The "Hopper" Dashboard
-------------------------

The main window serves as a centralized processing funnel.

Drag & Drop Zone
~~~~~~~~~~~~~~~~
The center panel ("EmptyStatePanel") listens for system drag events.
* **Supported Formats:** `.xlsx`, `.xls` (Excel 97-2003), `.ods` (LibreOffice), `.csv`, `.txt`, and `.dbf`.
* **Behavior:** Dropping files instantly generates a **Sheet Card** for each valid document. Invalid files are silently rejected with a `WARN` log entry.

Global Toolbar (Footer)
~~~~~~~~~~~~~~~~~~~~~~~
Located at the bottom of the workspace, controls the bulk export strategy.

* **Merge Switch (Consolidation):**
    * **OFF (Default):** Individual processing. 1 Source File = 1 Output File.
    * **ON:** Activates the consolidation algorithm.
        * *Logic:* The **first file** in the list becomes the "Master Schema".
        * *Alignment:* Subsequent files attempt to align their data to the Master Schema based on column index.
* **Master Format Dropdown:** Overrides the output format for ALL active cards (DBF, Excel, CSV).

2. Sheet Cards (Control Units)
------------------------------

Each imported file is represented by a `SheetCard` containing its configuration state.

Visual Feedback System (The Traffic Light)
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
The ``VisualCardHelper`` manages the card's state using a strict color code:

+-----------+--------+-----------------------------------------------------------------------+
| State     | Color  | Technical Meaning                                                     |
+===========+========+=======================================================================+
| **READY** | Cyan   | Structure analyzed. Waiting for user command.                         |
| **RISK** | Orange | **Data Truncation Alert.** A value in the sample exceeds the          |
|           |        | defined column length. Export may result in data loss.                |
| **PROC** | Blue   | Export thread is active. Writing to disk.                             |
| **OK** | Green  | Export finished with **100% Integrity**.                              |
| **WARN** | Amber  | Finished, but data mutations occurred (rounding, forced truncation).  |
| **ERROR** | Red    | Critical I/O Failure (File locked or Disk Full).                      |
+-----------+--------+-----------------------------------------------------------------------+

3. Live Debugging Editor
------------------------

Expanding a Sheet Card reveals the **Structure Editor**.

* **Debounce Timer:** The system waits 0.5 seconds after your last click/keystroke to validate.
* **Real-time Validation:** If you change a column from `Length: 50` to `Length: 10`, the system scans the memory sample.
    * If it finds the value "ADMINISTRATION" (14 chars), the row turns **RED** immediately.
    * This prevents runtime errors by catching them at configuration time.

4. Data Types Reference
-----------------------

The engine normalizes all inputs to these 6 primitives:

* **Character (0):** Alphanumeric text. Max 254 chars (DBF limit).
* **Numeric (1):** Floating point numbers.
* **Integer (2):** Strict Long Integers (decimals truncated).
* **Date (3):** Normalized to ISO `YYYY-MM-DD`.
* **Logical (4):** Boolean. Detects: `T/F`, `Y/N`, `1/0`, `Yes/No`.
* **Time (5):** Duration. Decodes Excel decimals (e.g., `0.5` -> `12:00:00`).
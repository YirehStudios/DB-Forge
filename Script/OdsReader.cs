/*
    DBF Forge - Data Forging Tool.
    Copyright (C) 2026 YirehStudios
    
    Description: 
    High-performance custom reader for Open Document Spreadsheet (ODS) files.
    It decompresses the ZIP container and streams 'content.xml' using XmlReader 
    to minimize memory footprint, avoiding full DOM loading.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Globalization;

namespace DbfForge.Core.Readers
{
    /// <summary>
    /// Lightweight and fast reader for .ODS files.
    /// Implements a forward-only stream reader for the internal XML structure.
    /// </summary>
    public class OdsReader : IDisposable
    {
        private ZipArchive _zipArchive;
        private XmlReader _xmlReader;
        
        // Public properties for reading state
        public string SheetName { get; private set; } = "Sheet1";
        public int FieldCount { get; private set; } = 0;
        
        private readonly List<object> _currentRow = new List<object>();
        
        // Buffer to handle row/column spans (merged cells)
        // Key: Column Index, Value: (Cell Value, Remaining Rows to Span)
        private readonly Dictionary<int, (object Value, int RemainingRows)> _spanBuffer = new Dictionary<int, (object, int)>();

        /// <summary>
        /// Initializes the reader by opening the ODS stream and locating content.xml.
        /// </summary>
        /// <param name="stream">Input file stream.</param>
        public OdsReader(Stream stream)
        {
            try 
            {
                // ODS format is a ZIP container. The actual data resides in "content.xml".
                _zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
                var entry = _zipArchive.GetEntry("content.xml");
                
                if (entry == null) 
                    throw new InvalidDataException("Invalid ODS file: content.xml not found.");

                // XmlReader is used instead of XmlDocument for performance and memory efficiency.
                var settings = new XmlReaderSettings { IgnoreWhitespace = true };
                _xmlReader = XmlReader.Create(entry.Open(), settings);
            } 
            catch (Exception ex)
            {
                throw new IOException("File is corrupt or unreadable.", ex);
            }
        }

        /// <summary>
        /// Advances the cursor to the next <table:table> element (Spreadsheet).
        /// </summary>
        /// <returns>True if a new sheet was found; otherwise, false.</returns>
        public bool MoveToNextSheet()
        {
            FieldCount = 0; 
            _spanBuffer.Clear();
            
            while (_xmlReader.Read())
            {
                // Look for the opening tag of a table
                if (_xmlReader.NodeType == XmlNodeType.Element && _xmlReader.LocalName == "table")
                {
                    SheetName = GetAttributeIgnoringNamespace("name") ?? "UnnamedSheet";
                    return true; 
                }
            }
            return false; 
        }

        /// <summary>
        /// Reads the next row (<table:table-row>) within the current sheet.
        /// </summary>
        /// <returns>True if a row with data was read; otherwise, false.</returns>
        public bool ReadRow()
        {
            _currentRow.Clear();

            while (_xmlReader.Read())
            {
                // Detect end of the current table/sheet
                if (_xmlReader.NodeType == XmlNodeType.EndElement && _xmlReader.LocalName == "table")
                {
                    _spanBuffer.Clear();
                    return false; 
                }

                // Detect start of a row
                if (_xmlReader.NodeType == XmlNodeType.Element && _xmlReader.LocalName == "table-row")
                {
                    ParseRowStructure();
                    
                    // If the row contains data, process it
                    if (_currentRow.Count > 0)
                    {
                        // Update maximum field count found so far
                        if (_currentRow.Count > FieldCount) FieldCount = _currentRow.Count;
                        
                        // Pad the row to match the maximum field count
                        while (_currentRow.Count < FieldCount) _currentRow.Add("");
                        
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Parses individual cells (<table:table-cell>) within the current row.
        /// Handles complex ODS features like 'number-columns-repeated' and 'number-rows-spanned'.
        /// </summary>
        private void ParseRowStructure()
        {
            // Create a subtree reader for the current row to isolate context
            using (var subReader = _xmlReader.ReadSubtree())
            {
                subReader.Read(); // Enter the <table-row> node
                
                int columnIndex = 0; // Tracks the actual visual column index

                while (subReader.Read())
                {
                    // Check if the current column index is occupied by a vertical span from a previous row
                    while (_spanBuffer.ContainsKey(columnIndex) && _spanBuffer[columnIndex].RemainingRows > 0)
                    {
                        // Inject the buffered value (phantom cell)
                        var spanInfo = _spanBuffer[columnIndex];
                        _currentRow.Add(spanInfo.Value);
                        
                        // Decrement the remaining life of the span
                        spanInfo.RemainingRows--;
                        
                        if (spanInfo.RemainingRows == 0) 
                            _spanBuffer.Remove(columnIndex);
                        else 
                            _spanBuffer[columnIndex] = spanInfo;

                        columnIndex++; // Move virtual cursor
                    }

                    // Process actual cell element
                    if (subReader.NodeType == XmlNodeType.Element && subReader.LocalName == "table-cell")
                    {
                        // Handle Horizontal Repetition
                        // Fix: Initialize with 1. Only overwrite if parse succeeds. 
                        // Previous logic set count to 0 on Parse failure causing data loss.
                        int repetitionCount = 1;
                        string sRep = GetAttributeIgnoringNamespace(subReader, "number-columns-repeated");
                        
                        if (!string.IsNullOrEmpty(sRep) && int.TryParse(sRep, out int parsedCount))
                        {
                            repetitionCount = parsedCount;
                        }

                        // Safety cap to prevent memory exhaustion on malformed files
                        if (repetitionCount > 1000) repetitionCount = 1; 

                        // Handle Vertical Spanning
                        int verticalSpan = 1;
                        string sSpan = GetAttributeIgnoringNamespace(subReader, "number-rows-spanned");
                        if (!string.IsNullOrEmpty(sSpan)) int.TryParse(sSpan, out verticalSpan);

                        object cellValue = ExtractTypedValue(subReader);

                        // Insert the value N times (Horizontal expansion)
                        for (int i = 0; i < repetitionCount; i++) 
                        {
                            // If vertical span > 1, store in buffer for future rows
                            if (verticalSpan > 1) 
                            {
                                // Store: (Value, Remaining Rows - 1 because current row counts)
                                _spanBuffer[columnIndex] = (cellValue, verticalSpan - 1);
                            }

                            _currentRow.Add(cellValue);
                            columnIndex++; // Move actual cursor

                            // Note: If horizontal repetition exists, subsequent columns might 
                            // also be blocked by the buffer, checked in the next iteration of the while loop.
                        }
                    }
                }
                
                // Edge Case: If the row ends but the buffer still has pending spans at the end
                while (_spanBuffer.ContainsKey(columnIndex) && _spanBuffer[columnIndex].RemainingRows > 0)
                {
                        var spanInfo = _spanBuffer[columnIndex];
                        _currentRow.Add(spanInfo.Value);
                        
                        spanInfo.RemainingRows--;
                        if (spanInfo.RemainingRows == 0) 
                        _spanBuffer.Remove(columnIndex);
                        else 
                        _spanBuffer[columnIndex] = spanInfo;
                        
                        columnIndex++;
                }
            }
        }
        /// <summary>
        /// Extracts and converts the cell value based on its XML attributes type.
        /// </summary>
        /// <param name="cellReader">The XmlReader positioned at the cell.</param>
        /// <returns>The typed object (DateTime, Double, Boolean, String).</returns>
        private object ExtractTypedValue(XmlReader cellReader)
        {
            string type = GetAttributeIgnoringNamespace(cellReader, "value-type");
            string rawValue = GetAttributeIgnoringNamespace(cellReader, "value");
            string rawDate = GetAttributeIgnoringNamespace(cellReader, "date-value");
            string rawTime = GetAttributeIgnoringNamespace(cellReader, "time-value");
            string rawBool = GetAttributeIgnoringNamespace(cellReader, "boolean-value");

            // 1. TIME HANDLING (ODS stores as ISO duration "PT12H30M00S")
            // Patch: Replaced manual parsing with XmlConvert for ISO 8601 compliance.
            if (type == "time" || !string.IsNullOrEmpty(rawTime)) 
            {
                try 
                {
                    // Normalize standard format if necessary (e.g. handle fractional seconds correctly)
                    TimeSpan ts = System.Xml.XmlConvert.ToTimeSpan(rawTime);
                    
                    // Return as formatted string HH:MM:SS matching DBF/Excel expectations
                    // Using "c" format (constant) or manual construction for strict HH:mm:ss
                    return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
                } 
                catch (FormatException) 
                {
                    // Fallback to raw string if format is non-compliant
                    return rawTime.Replace("PT", "");
                }
            }

            // 2. DATE HANDLING
            if (type == "date" && !string.IsNullOrEmpty(rawDate)) 
            {
                // Strip Z (UTC) marker to prevent timezone shifts during parse if not desired
                rawDate = rawDate.Replace("Z", "");
                if (DateTime.TryParse(rawDate, out DateTime dt)) return dt;
            }

            // 3. NUMERIC HANDLING
            if ((type == "float" || type == "currency") && !string.IsNullOrEmpty(rawValue)) 
            {
                if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
            }

            // 4. BOOLEAN HANDLING
            if (type == "boolean" && !string.IsNullOrEmpty(rawBool)) 
            {
                return (rawBool.ToLower() == "true");
            }

            // 5. TEXT (Default fallback)
            string textContent = "";
            using (var inner = cellReader.ReadSubtree()) 
            {
                while (inner.Read()) 
                {
                    if (inner.NodeType == XmlNodeType.Text || inner.NodeType == XmlNodeType.CDATA) 
                        textContent += inner.Value;
                }
            }
            return textContent;
        }

        /// <summary>
        /// Helper to retrieve XML attributes ignoring their namespace prefix.
        /// </summary>
        private string GetAttributeIgnoringNamespace(string localName) => GetAttributeIgnoringNamespace(_xmlReader, localName);
        
        private string GetAttributeIgnoringNamespace(XmlReader reader, string localName)
        {
            if (!reader.HasAttributes) return null;
            
            for (int i = 0; i < reader.AttributeCount; i++) 
            {
                reader.MoveToAttribute(i);
                if (reader.LocalName == localName) 
                {
                    string val = reader.Value;
                    reader.MoveToElement();
                    return val;
                }
            }
            reader.MoveToElement();
            return null;
        }

        /// <summary>
        /// Retrieves the value at the specified column index for the current row.
        /// </summary>
        public object GetValue(int index) 
        {
            if (index >= 0 && index < _currentRow.Count) return _currentRow[index];
            return "";
        }

        public void Dispose() 
        {
            _xmlReader?.Dispose();
            _zipArchive?.Dispose();
        }
    }
}
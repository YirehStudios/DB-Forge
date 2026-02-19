/*
    DBF Forge - Data Forging Tool.
    Copyright (C) 2026 YirehStudios
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel; // For .xlsx
using NPOI.HSSF.UserModel; // For .xls

namespace DbfForge.Core.Readers
{
    /// <summary>
    /// Advanced Excel reader implementation using NPOI.
    /// Features:
    /// - Formula evaluation (retrieves calculated values).
    /// - Robust error handling for corrupt data types.
    /// - Automatic resolution of merged regions.
    /// - Recovery of negative time values displayed as "#####" in Excel.
    /// </summary>
    public class ExcelReaderNPOI : IDisposable
    {
        private IWorkbook _workbook;
        private ISheet _currentSheet;
        private int _sheetIndex = -1;
        private IEnumerator _rowEnumerator;
        
        private readonly DataFormatter _dataFormatter = new DataFormatter();
        private Dictionary<string, ICell> _mergedCellsMap;

        public int FieldCount { get; private set; }
        public string SheetName => _currentSheet?.SheetName ?? "";

        /// <summary>
        /// Initializes the workbook based on the file extension.
        /// </summary>
        /// <param name="stream">File stream.</param>
        /// <param name="extension">File extension (.xls or .xlsx).</param>
        public ExcelReaderNPOI(Stream stream, string extension)
        {
            if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                _workbook = new XSSFWorkbook(stream);
            else if (extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
                _workbook = new HSSFWorkbook(stream);
            else
                throw new NotSupportedException("Format not supported by NPOI reader.");
        }

        /// <summary>
        /// Advances to the next sheet in the workbook.
        /// </summary>
        /// <returns>True if a sheet exists; otherwise, false.</returns>
        public bool MoveToNextSheet()
        {
            _sheetIndex++;
            if (_workbook == null || _sheetIndex >= _workbook.NumberOfSheets) return false;

            _currentSheet = _workbook.GetSheetAt(_sheetIndex);
            _rowEnumerator = _currentSheet.GetRowEnumerator(); 
            
            BuildMergedRegionsMap();
            FieldCount = 0;
            return true;
        }

        /// <summary>
        /// Reads the next row from the current sheet.
        /// </summary>
        /// <returns>True if the row exists.</returns>
        public bool ReadRow()
        {
            if (_rowEnumerator != null && _rowEnumerator.MoveNext())
            {
                var row = _rowEnumerator.Current as IRow;
                if (row != null && row.LastCellNum > FieldCount) 
                    FieldCount = row.LastCellNum;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Safely retrieves the cell value at the given index.
        /// Wrapped in a try-catch to prevent application crash on single corrupt cells.
        /// </summary>
        public object GetValue(int index)
        {
            try 
            {
                var currentRow = _rowEnumerator?.Current as IRow;
                if (currentRow == null) return null;
                
                int rowIndex = currentRow.RowNum;
                var cell = currentRow.GetCell(index);

                // Handle Merged Cells: Redirect to the parent cell
                string mergeKey = $"{rowIndex}_{index}";
                if (_mergedCellsMap.ContainsKey(mergeKey)) 
                    cell = _mergedCellsMap[mergeKey]; 

                if (cell == null) return "";

                // --- FORMULA HANDLING ---
                if (cell.CellType == CellType.Formula)
                {
                    // Return error string if formula calculation failed
                    if (cell.CachedFormulaResultType == CellType.Error) return "#ERR";
                    
                    // Handle numeric results from formulas (e.g., Sum of Hours)
                    if (cell.CachedFormulaResultType == CellType.Numeric) 
                    {
                         double numericValue = cell.NumericCellValue;

                         // Special Case: Negative time values.
                         // Excel masks these as "#####", but we recover the value manually.
                         if (numericValue < 0 && DateUtil.IsCellDateFormatted(cell)) 
                         {
                             return FormatNegativeTimeSpan(numericValue);
                         }

                         // Retrieve format string from the Workbook to maintain visual consistency
                         string formatStr = cell.Sheet.Workbook.CreateDataFormat().GetFormat(cell.CellStyle.DataFormat);
                         return _dataFormatter.FormatRawCellContents(numericValue, cell.CellStyle.DataFormat, formatStr);
                    }
                }
                // --- NUMERIC CELL HANDLING ---
                else if (cell.CellType == CellType.Numeric) 
                {
                    // Check for negative time values directly in numeric cells
                    if (cell.NumericCellValue < 0 && DateUtil.IsCellDateFormatted(cell)) 
                    {
                        return FormatNegativeTimeSpan(cell.NumericCellValue);
                    }
                }

                // Default formatter handles strings, booleans, and standard dates safely
                return _dataFormatter.FormatCellValue(cell);
            } 
            catch 
            {
                // Return empty string on corruption to allow the process to continue
                return ""; 
            }
        }

        /// <summary>
        /// Converts Excel's decimal time representation (e.g., -0.6) into a readable time string (e.g., "-14:24:00").
        /// </summary>
        private string FormatNegativeTimeSpan(double excelValue) 
        {
            try 
            {
                TimeSpan ts = TimeSpan.FromDays(excelValue);
                string sign = ts.TotalSeconds < 0 ? "-" : "";
                ts = ts.Duration(); // Work with absolute value for formatting
                return $"{sign}{(int)Math.Floor(ts.TotalHours)}:{ts.Minutes:00}:{ts.Seconds:00}";
            } 
            catch 
            { 
                return excelValue.ToString(); 
            }
        }

        /// <summary>
        /// Pre-calculates a map of all merged regions in the sheet.
        /// Maps every cell within a merged region to its top-left "parent" cell.
        /// </summary>
        private void BuildMergedRegionsMap()
        {
            _mergedCellsMap = new Dictionary<string, ICell>();
            
            for (int i = 0; i < _currentSheet.NumMergedRegions; i++)
            {
                var region = _currentSheet.GetMergedRegion(i);
                var parentRow = _currentSheet.GetRow(region.FirstRow);
                var parentCell = parentRow?.GetCell(region.FirstColumn);

                if (parentCell != null)
                {
                    // Iterate through all cells in the region
                    for (int r = region.FirstRow; r <= region.LastRow; r++)
                    {
                        for (int c = region.FirstColumn; c <= region.LastColumn; c++)
                        {
                            // Skip the parent cell itself
                            if (r == region.FirstRow && c == region.FirstColumn) continue;
                            
                            // Map coordinate to parent cell
                            _mergedCellsMap[$"{r}_{c}"] = parentCell;
                        }
                    }
                }
            }
        }

        public void Dispose() 
        { 
            _workbook?.Close(); 
        }
    }
}
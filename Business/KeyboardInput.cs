﻿using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SystemTrayMenu.DataClasses;
using SystemTrayMenu.Helper;
using SystemTrayMenu.Utilities;
using Menu = SystemTrayMenu.UserInterface.Menu;

namespace SystemTrayMenu.Handler
{
    internal class KeyboardInput : IDisposable
    {
        public event EventHandlerEmpty HotKeyPressed;
        public event EventHandlerEmpty ClosePressed;
#warning use event not action
        public event Action<Keys> KeyPressedSearching;
        public event Action<DataGridView, int> RowSelected;
        public event Action<int, int, DataGridView> RowDeselected;
        public event EventHandlerEmpty Cleared;

        private readonly Menu[] menus;
        private readonly KeyboardHook hook = new KeyboardHook();
        private readonly Timer timerKeySearch = new Timer();
        public int iRowKey = -1;
        public int iMenuKey = 0;
        private string KeySearchString = string.Empty;

        public bool InUse = false;

        public KeyboardInput(Menu[] menus)
        {
            this.menus = menus;

            timerKeySearch.Interval = MenuDefines.KeySearchInterval;
            timerKeySearch.Tick += TimerKeySearch_Tick;
            void TimerKeySearch_Tick(object sender, EventArgs e)
            {
                // this search has expired, reset search
                timerKeySearch.Stop();
                KeySearchString = string.Empty;
            }
        }

        public void Dispose()
        {
            hook.Dispose();
            timerKeySearch.Dispose();
        }

        private int GetMenuIndex(in Menu currentMenu)
        {
            int index = 0;
            foreach (Menu menuFindIndex in menus.Where(m => m != null))
            {
                if (currentMenu == menuFindIndex)
                {
                    break;
                }
                index++;
            }
            return index;
        }

        internal void RegisterHotKey()
        {
            if (!string.IsNullOrEmpty(Properties.Settings.Default.HotKey))
            {
                try
                {
                    hook.RegisterHotKey(Properties.Settings.Default.HotKey);
                    hook.KeyPressed += hook_KeyPressed;
                    void hook_KeyPressed(object sender, KeyPressedEventArgs e)
                    {
                        HotKeyPressed?.Invoke();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    //Log.Error($"key:'{key}'", ex);
                    Properties.Settings.Default.HotKey = string.Empty;
                    Properties.Settings.Default.Save();
                    MessageBox.Show(ex.Message);
                }
            }
        }

        internal void ResetSelectedByKey()
        {
            iRowKey = -1;
            iMenuKey = 0;
        }

        internal void CmdKeyProcessed(object sender, Keys keys)
        {
            switch (keys)
            {
                case Keys.Enter:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Escape:
                    SelectByKey(keys);
                    break;
                case Keys.Control | Keys.F:
                    Menu menu = menus[iMenuKey];
                    menu.FocusTextBox();
                    break;
                case Keys.Tab:
                    {
                        Menu currentMenu = (Menu)sender;
                        int indexOfTheCurrentMenu = GetMenuIndex(currentMenu);
                        int indexMax = menus.Where(m => m != null).Count() - 1;
                        int indexNew = 0;
                        if (indexOfTheCurrentMenu > 0)
                        {
                            indexNew = indexOfTheCurrentMenu - 1;
                        }
                        else
                        {
                            indexNew = indexMax;
                        }

                        menus[indexNew].FocusTextBox();
                    }
                    break;
                case Keys.Tab | Keys.Shift:
                    {
                        Menu currentMenu = (Menu)sender;
                        int indexOfTheCurrentMenu = GetMenuIndex(currentMenu);
                        int indexMax = menus.Where(m => m != null).Count() - 1;
                        int indexNew = 0;
                        if (indexOfTheCurrentMenu < indexMax)
                        {
                            indexNew = indexOfTheCurrentMenu + 1;
                        }
                        else
                        {
                            indexNew = 0;
                        }

                        menus[indexNew].FocusTextBox();
                    }
                    break;
                case Keys.Apps:
                    {
                        DataGridView dgv = menus[iMenuKey].GetDataGridView();

                        if (iRowKey > -1 &&
                            dgv.Rows.Count > iRowKey)
                        {
                            Point pt = dgv.GetCellDisplayRectangle(2, iRowKey, false).Location;
                            RowData trigger = (RowData)dgv.Rows[iRowKey].Cells[2].Value;
                            MouseEventArgs mea = new MouseEventArgs(MouseButtons.Right, 1, pt.X, pt.Y, 0);
                            trigger.MouseDown(dgv, mea);
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// While menu is open user presses a key to search for specific entries.
        /// </summary>
        /// <param name="sender">not used</param>
        /// <param name="e">Key data of the pressed key</param>
        internal void KeyPress(object sender, KeyPressEventArgs e)
        {
            if (char.IsLetterOrDigit(e.KeyChar) ||
                char.IsPunctuation(e.KeyChar) ||
                char.IsWhiteSpace(e.KeyChar) ||
                char.IsSeparator(e.KeyChar))
            {
                string letter = e.KeyChar.ToString(CultureInfo.InvariantCulture);

                Menu menu = menus[iMenuKey];
                menu.KeyPressedSearch(letter);

#warning remove if not more needed
                // Old Search by letters
                //timerKeySearch.Stop();
                //if (string.IsNullOrEmpty(KeySearchString))
                //{
                //    // no search string set, start new search with initial letter
                //    KeySearchString += letter;
                //    SelectByKey(Keys.None, KeySearchString);
                //}
                //else if (KeySearchString.Length == 1 && KeySearchString.LastOrDefault().ToString(CultureInfo.InvariantCulture) == letter)
                //{
                //    // initial letter pressed again, jump to next element
                //    SelectByKey(Keys.None, letter);
                //}
                //else
                //{
                //    // new character for the search string, narrow down the search
                //    KeySearchString += letter;
                //    SelectByKey(Keys.None, KeySearchString, true);
                //}
                //// give user some time to continue with this search
                //timerKeySearch.Start();

                e.Handled = true;
            }
        }

        internal void SearchTextChanging()
        {
            ClearIsSelectedByKey();
        }

        internal void SearchTextChanged(object sender, EventArgs e)
        {
            Menu menu = (Menu)sender;
            DataGridView dgv = menu.GetDataGridView();
            if (dgv.Rows.Count > 0)
            {
                Select(dgv, 0);
            }
        }

        private bool IsAnyMenuSelectedByKey(
            ref DataGridView dgv,
            ref Menu menuFromSelected,
            ref string textselected)
        {
            Menu menu = menus[iMenuKey];
            bool isStillSelected = false;
            if (menu != null &&
                iRowKey > -1)
            {
                dgv = menu.GetDataGridView();
                if (dgv.Rows.Count > iRowKey)
                {
                    RowData rowData = (RowData)dgv.
                        Rows[iRowKey].Cells[2].Value;
                    if (rowData.IsSelectedByKeyboard)
                    {
                        isStillSelected = true;
                        menuFromSelected = rowData.SubMenu;
                        textselected = dgv.Rows[iRowKey].
                            Cells[1].Value.ToString();
                    }
                }
            }

            return isStillSelected;
        }

        private void SelectByKey(Keys keys, string keyInput = "", bool KeepSelection = false)
        {
            int iRowBefore = iRowKey;
            int iMenuBefore = iMenuKey;

            Menu menu = menus[iMenuKey];
            DataGridView dgv = null;
            DataGridView dgvBefore = null;
            Menu menuFromSelected = null;
            string textselected = string.Empty;
            bool isStillSelected = IsAnyMenuSelectedByKey(ref dgv, ref menuFromSelected, ref textselected);
            if (isStillSelected)
            {
                if (KeepSelection)
                {
                    // If current selection is still valid for this search then skip selecting different item
                    if (textselected.StartsWith(keyInput, true, CultureInfo.InvariantCulture))
                    {
                        return;
                    }
                }

                dgvBefore = dgv;
            }
            else
            {
                ResetSelectedByKey();
                menu = menus[iMenuKey];
                dgv = menu.GetDataGridView();
            }

            bool toClear = false;
            switch (keys)
            {
                case Keys.Enter:
                    if (iRowKey > -1 &&
                        dgv.Rows.Count > iRowKey)
                    {
                        RowData trigger = (RowData)dgv.Rows[iRowKey].Cells[2].Value;
                        if (trigger.IsSelected || !trigger.ContainsMenu)
                        {
                            trigger.MouseDown(dgv, null);
                            trigger.DoubleClick(
                                new MouseEventArgs(MouseButtons.Left, 0, 0, 0, 0));
                        }
                        else
                        {
                            RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                            SelectRow(dgv, iRowKey);
                        }
                    }
                    break;
                case Keys.Up:
                    if (SelectMatchedReverse(dgv, iRowKey) ||
                        SelectMatchedReverse(dgv, dgv.Rows.Count - 1))
                    {
                        RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                        SelectRow(dgv, iRowKey);
                        toClear = true;
                    }
                    break;
                case Keys.Down:
                    if (SelectMatched(dgv, iRowKey) ||
                        SelectMatched(dgv, 0))
                    {
                        RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                        SelectRow(dgv, iRowKey);
                        toClear = true;
                    }
                    break;
                case Keys.Left:
                    int iMenuKeyNext = iMenuKey + 1;
                    if (isStillSelected)
                    {
                        if (menuFromSelected != null &&
                            menuFromSelected == menus[iMenuKeyNext])
                        {
                            dgv = menuFromSelected.GetDataGridView();
                            if (dgv.Rows.Count > 0)
                            {
                                iMenuKey += 1;
                                iRowKey = -1;
                                if (SelectMatched(dgv, iRowKey) ||
                                    SelectMatched(dgv, 0))
                                {
                                    RowDeselected(iMenuBefore,
                                        iRowBefore, dgvBefore);
                                    SelectRow(dgv, iRowKey);
                                    toClear = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        iRowKey = -1;
                        iMenuKey = menus.Where(m => m != null).Count() - 1;
                        if (menus[iMenuKey] != null)
                        {
                            dgv = menus[iMenuKey].GetDataGridView();
                            if (SelectMatched(dgv, iRowKey) ||
                                SelectMatched(dgv, 0))
                            {
                                RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                                SelectRow(dgv, iRowKey);
                                toClear = true;
                            }
                        }
                    }
                    break;
                case Keys.Right:
                    if (iMenuKey > 0)
                    {
                        if (menus[iMenuKey - 1] != null)
                        {
                            iMenuKey -= 1;
                            iRowKey = -1;
                            menu = menus[iMenuKey];
                            dgv = menu.GetDataGridView();
                            if (SelectMatched(dgv, dgv.SelectedRows[0].Index) ||
                                SelectMatched(dgv, 0))
                            {
                                RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                                SelectRow(dgv, iRowKey);
                                toClear = true;
                            }
                        }
                    }
                    else
                    {
                        RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                        iMenuKey = 0;
                        iRowKey = -1;
                        toClear = true;
                        Cleared?.Invoke();
                    }
                    break;
                case Keys.Escape:
                    RowDeselected(iMenuBefore, iRowBefore, dgvBefore);
                    iMenuKey = 0;
                    iRowKey = -1;
                    toClear = true;
                    ClosePressed?.Invoke();
                    break;
                default:
                    if (!string.IsNullOrEmpty(keyInput))
                    {
                        if (SelectMatched(dgv, iRowKey, keyInput) ||
                            SelectMatched(dgv, 0, keyInput))
                        {
                            RowDeselected(iMenuBefore, iRowBefore, null);
                            SelectRow(dgv, iRowKey);
                            toClear = true;
                        }
                        else if (isStillSelected)
                        {
                            iRowKey = iRowBefore - 1;
                            if (SelectMatched(dgv, iRowKey, keyInput) ||
                                SelectMatched(dgv, 0, keyInput))
                            {
                                RowDeselected(iMenuBefore, iRowBefore, null);
                                SelectRow(dgv, iRowKey);
                            }
                            else
                            {
                                iRowKey = iRowBefore;
                            }
                        }
                    }
                    break;
            }
            if (isStillSelected && toClear)
            {
                ClearIsSelectedByKey(iMenuBefore, iRowBefore);
            }
        }

        private void SelectRow(DataGridView dgv, int iRowKey)
        {
            InUse = true;
            RowSelected(dgv, iRowKey);
        }

        private bool SelectMatched(DataGridView dgv,
            int indexStart, string keyInput = "")
        {
            bool found = false;
            for (int i = indexStart; i < dgv.Rows.Count; i++)
            {
                if (Select(dgv, i, keyInput))
                {
                    found = true;
                    break;
                }
            }
            return found;
        }

        private bool SelectMatchedReverse(DataGridView dgv,
            int indexStart, string keyInput = "")
        {
            bool found = false;
            for (int i = indexStart; i > -1; i--)
            {
                if (Select(dgv, i, keyInput))
                {
                    found = true;
                    break;
                }
            }
            return found;
        }

        public void Select(DataGridView dgv, int i)
        {
            int newiMenuKey = ((Menu)dgv.TopLevelControl).Level;
            if (i != iRowKey || newiMenuKey != iMenuKey)
            {
                ClearIsSelectedByKey();
            }
            iRowKey = i;
            iMenuKey = newiMenuKey;
            DataGridViewRow row = dgv.Rows[i];
            RowData rowData = (RowData)row.Cells[2].Value;
            rowData.IsSelectedByKeyboard = true;
            row.Selected = false; //event trigger
            row.Selected = true; //event trigger
        }

        private bool Select(DataGridView dgv, int i,
            string keyInput = "")
        {
            bool found = false;
            if (i > -1 &&
                i != iRowKey &&
                dgv.Rows.Count > i)
            {
                DataGridViewRow row = dgv.Rows[i];
                RowData rowData = (RowData)row.Cells[2].Value;
                string text = row.Cells[1].Value.ToString();
                if (text.StartsWith(keyInput, true, CultureInfo.InvariantCulture))
                {
                    iRowKey = rowData.RowIndex;
                    rowData.IsSelectedByKeyboard = true;
                    row.Selected = false; //event trigger
                    row.Selected = true; //event trigger
                    if (row.Index < dgv.FirstDisplayedScrollingRowIndex)
                    {
                        dgv.FirstDisplayedScrollingRowIndex = row.Index;
                    }
                    else if (row.Index >=
                        dgv.FirstDisplayedScrollingRowIndex +
                        dgv.DisplayedRowCount(false))
                    {
                        dgv.FirstDisplayedScrollingRowIndex = row.Index -
                        dgv.DisplayedRowCount(false) + 1;
                    }

                    found = true;
                }
            }
            return found;
        }

        internal void ClearIsSelectedByKey()
        {
            ClearIsSelectedByKey(iMenuKey, iRowKey);
        }

        private void ClearIsSelectedByKey(int menuIndex, int rowIndex)
        {
            Menu menu = menus[menuIndex];
            if (menu != null && rowIndex > -1)
            {
                DataGridView dgv = menu.GetDataGridView();
                if (dgv.Rows.Count > rowIndex)
                {
                    DataGridViewRow row = dgv.Rows[rowIndex];
                    RowData rowData = (RowData)row.Cells[2].Value;
                    rowData.IsSelectedByKeyboard = false;
                    row.Selected = false; //event trigger
                }
            }
        }
    }
}
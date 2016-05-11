using System;
using System.Windows.Forms;

namespace DemoMdictReader
{
    public partial class Form1 : Form
    {
        Mdx _dict;
        public Form1()
        {
            InitializeComponent();
        }

        private void btnInitialize_Click(object sender, EventArgs e)
        {
            _dict = new Mdx(txtDictFile.Text);
            _dict.GetStyleSheets();
            _dict.GetKeys();
            _dict.IgnoreKeys();
            _dict.GetRecordBlocksInfo();

            MessageBox.Show(@"Ok well done!", @"Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                txtDictFile.Text = openFileDialog.FileName;
            }
        }

        private void btnGetValue_Click(object sender, EventArgs e)
        {
            wbResult.DocumentText = _dict.GetKeyValue(new MdictHelper.Tuple<long, long, string>(int.Parse(txtValue1.Text), int.Parse(txtValue2.Text), txtValue3.Text));
        }
    }
}

using SharpMonoInjector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SharpMonoInjector.Gui
{
    public partial class Form1 : Form
    {
        private IntPtr _injectedAssembly = IntPtr.Zero;
        private const string ConfigFilePath = "inject_config.ini";

        public Form1()
        {
            InitializeComponent();
            txtNamespace.TextChanged += TxtNamespace_TextChanged;
            // 【核心】仅用代码隐藏按钮，不动设计器
            btnEject.Visible = false;
            btnEject.Enabled = false;
            RefreshProcesses();
            LoadConfig();
        }

        // 自动移除命名空间输入框里的 .dll 后缀
        private void TxtNamespace_TextChanged(object sender, EventArgs e)
        {
            string text = txtNamespace.Text.Trim();
            if (text.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                txtNamespace.TextChanged -= TxtNamespace_TextChanged;
                txtNamespace.Text = text.Substring(0, text.Length - 4);
                txtNamespace.SelectionStart = txtNamespace.Text.Length;
                txtNamespace.TextChanged += TxtNamespace_TextChanged;
            }
        }

        private void RefreshProcesses()
        {
            cmbProcesses.DataSource = null;
            cmbProcesses.Items.Clear();

            List<SharpMonoInjector.ProcessItem> monoProcesses;
            try
            {
                monoProcesses = ProcessUtils.GetMonoProcesses();
            }
            catch
            {
                monoProcesses = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .OrderBy(p => p.ProcessName)
                    .Select(p => new SharpMonoInjector.ProcessItem(p.ProcessName, p.Id))
                    .ToList();
            }

            cmbProcesses.DataSource = monoProcesses;
            cmbProcesses.DisplayMember = "DisplayText";
            cmbProcesses.ValueMember = "Id";

            if (monoProcesses.Count > 0)
            {
                cmbProcesses.SelectedIndex = 0;
            }
        }

#pragma warning disable IDE1006
        private void btnRefresh_Click(object sender, EventArgs e)
#pragma warning restore IDE1006
        {
            RefreshProcesses();
        }

#pragma warning disable IDE1006
        private void btnBrowse_Click(object sender, EventArgs e)
#pragma warning restore IDE1006
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Assembly Files (*.dll)|*.dll|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtAssemblyPath.Text = ofd.FileName;
                }
            }
        }

#pragma warning disable IDE1006
        private void btnInject_Click(object sender, EventArgs e)
#pragma warning restore IDE1006
        {
            try
            {
                if (cmbProcesses.SelectedItem == null)
                {
                    MessageBox.Show("请选择一个进程", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtAssemblyPath.Text) || !File.Exists(txtAssemblyPath.Text))
                {
                    MessageBox.Show("请选择有效的程序集文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtNamespace.Text))
                {
                    MessageBox.Show("请输入命名空间", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtClassName.Text))
                {
                    MessageBox.Show("请输入类名", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(txtMethodName.Text))
                {
                    MessageBox.Show("请输入方法名", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var processItem = (SharpMonoInjector.ProcessItem)cmbProcesses.SelectedItem;
                byte[] assemblyBytes = File.ReadAllBytes(txtAssemblyPath.Text);

                using (var injector = new Injector(processItem.Id))
                {
                    _injectedAssembly = injector.Inject(
                        assemblyBytes,
                        txtNamespace.Text,
                        txtClassName.Text,
                        txtMethodName.Text);
                }

                lblStatus.Text = "注入成功！";
                lblStatus.ForeColor = System.Drawing.Color.Green;
                // 【核心】移除启用卸载按钮的代码
                SaveConfig();
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"注入失败: {ex.Message}";
                lblStatus.ForeColor = System.Drawing.Color.Red;
                MessageBox.Show($"注入失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 保留卸载代码，但按钮已隐藏，不会被触发
#pragma warning disable IDE1006
        private void btnEject_Click(object sender, EventArgs e)
#pragma warning restore IDE1006
        {
            // 功能已隐藏，此处代码不会执行
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var lines = File.ReadAllLines(ConfigFilePath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length != 2) continue;

                        switch (parts[0].Trim())
                        {
                            case "Namespace":
                                txtNamespace.Text = parts[1].Trim();
                                break;
                            case "ClassName":
                                txtClassName.Text = parts[1].Trim();
                                break;
                            case "MethodName":
                                txtMethodName.Text = parts[1].Trim();
                                break;
                            case "LastAssemblyPath":
                                if (File.Exists(parts[1].Trim()))
                                    txtAssemblyPath.Text = parts[1].Trim();
                                break;
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                List<string> lines = new List<string>
                {
                    $"Namespace={txtNamespace.Text.Trim()}",
                    $"ClassName={txtClassName.Text.Trim()}",
                    $"MethodName={txtMethodName.Text.Trim()}",
                    $"LastAssemblyPath={txtAssemblyPath.Text.Trim()}"
                };
                File.WriteAllLines(ConfigFilePath, lines);
            }
            catch { }
        }
    }
}
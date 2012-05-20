using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data;
using System.Threading;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace WinMD5
{
	/// <summary>
	/// Summary description for Form1.
	/// </summary>
	public class WinMD5Form : System.Windows.Forms.Form
	{
		// We store our version here so that we don't have to update 3 different places
		// every time we change the version.
		public static string version="v2.07";

		protected MTQueue queue=new MTQueue();

		protected Thread workerThread;
		protected bool quitting=false;
		private System.Windows.Forms.ProgressBar progressBar;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.MainMenu mainMenu;
		private System.Windows.Forms.MenuItem menuItem1;
		private System.Windows.Forms.MenuItem menuItem5;
		private System.Windows.Forms.MenuItem menuItem6;
		private System.Windows.Forms.MenuItem quitItem;
		private System.Windows.Forms.MenuItem menuItem7;
		private System.Windows.Forms.Label currentlyProcessingLabel;
		private System.Windows.Forms.Label label3;
		private System.ComponentModel.IContainer components;
		private System.Windows.Forms.ListView listView;
		private System.Windows.Forms.ColumnHeader pathHeader;
		private System.Windows.Forms.ColumnHeader hashHeader;
		private System.Windows.Forms.ColumnHeader verifiedHeader;

		private DataTable dtable=new DataTable();
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label knownHashesLabel;
		private System.Windows.Forms.Button clearButton;
		private System.Windows.Forms.LinkLabel webLink;
		private System.Windows.Forms.MenuItem openFileItem;
		private System.Windows.Forms.MenuItem openMD5Item;
		private System.Windows.Forms.MenuItem saveMD5Item;
		private System.Windows.Forms.MenuItem aboutItem;
		private System.Windows.Forms.OpenFileDialog openFileDialog;
		private System.Windows.Forms.SaveFileDialog saveFileDialog;
		private System.Windows.Forms.PictureBox pictureBox;
		
		private Hashtable hashDatabase=Hashtable.Synchronized(new Hashtable());
		private System.Windows.Forms.Timer alertTimer;
		private System.Windows.Forms.Label alertLabel;
		private System.Windows.Forms.ColumnHeader sizeHeader;

		protected int hashErrors=0;

		protected const int COLUMN_PATH=0,COLUMN_HASH=1,COLUMN_SIZE=2,COLUMN_STATUS=3;

		protected bool reverseSort=false;
		private System.Windows.Forms.MenuItem menuItem2;
		private System.Windows.Forms.MenuItem copyItem;
		private System.Windows.Forms.Label enqueuedLabel;
		private System.Windows.Forms.Button abortButton;
		protected int reverseSortLastColumn=0;
        private MenuItem menuItem3;
        private MenuItem alwaysOnTopItem;
        private MenuItem useCRLF;

		protected ProgressStream progStream;

		/** Every file that has been dragged into our window gets a QueueItem
		 * and is thrown into a queue for a background thread to process later.
		 */
		public class QueueItem
		{
			// The absolute path name
			public string fullpath; 

			// Just the name of the file, without the dirs
			public string partialpath; 

			// size in bytes of the file
			public long size; 

			public QueueItem(string fullpath, string partialpath)
			{
				this.fullpath=fullpath;
				this.partialpath=partialpath;
				this.size=0;
			}

			public override string ToString()
			{
				return partialpath;
			}
		}

		public WinMD5Form()
		{
			//
			// Required for Windows Form Designer support
			//
			InitializeComponent();

			//
			// TODO: Add any constructor code after InitializeComponent call
			//
			workerThread=new Thread(new ThreadStart(WorkerThreadProc));
			workerThread.Priority=ThreadPriority.BelowNormal;
			workerThread.Start();
		
			// set the version dynamically
			this.Text="WinMD5 "+version+" (C) 2003-2006 by eolson@mit.edu";

			// set the alert icon
			Bitmap bmap=new Bitmap(pictureBox.Image);
            bmap.MakeTransparent();
//			bmap.MakeTransparent(Color.Black);
			pictureBox.Image=bmap;
			}

		protected delegate void AddFileToGridDelegate(QueueItem item, string hash);

		/** We've hashed a file, now we want to add it to the list on-screen
		 */
		protected void AddFileToGrid(QueueItem item, string hash)
		{
			string dbhash=LookupHash(item.partialpath);
			string verified;

			Debug.Assert(this.InvokeRequired==false);

			if (dbhash!=null)
			{
				if (dbhash==hash)
					verified="Good";
				else
					verified="BAD";
			}
			else
				verified="Unknown";

			if (IsMD5File(item.partialpath))
				verified="Loaded";

			ListViewItem lvi=new ListViewItem(new string[] {item.partialpath, hash, ""+item.size, verified});
			if (verified=="BAD")
			{
				lvi.BackColor=Color.Yellow;
				hashErrors++;
			}

			listView.Items.Add(lvi);
	//		listView.EnsureVisible(listView.Items.Count-1);

			EnableAlert(hashErrors>0);
		}

		/** Return the md5 hash for a path, or null if we don't have it. **/
		protected string LookupHash(string path)
		{
			string dbhash=(string) hashDatabase[path];
			if (dbhash==null)
				dbhash=(string) hashDatabase[JustTheFileName(path)];

			return (string) dbhash;
		}

		/** Enable the blinking Alert! button. */
		protected void EnableAlert(bool v)
		{
			pictureBox.Visible=v;
			alertLabel.Visible=v;
			alertTimer.Enabled=v;
		}

		/** Recheck all of the previously hashed files against our database of
		 * good hashes. We call this when we load a new MD5SUM file.
		 **/
		protected void ReverifyAllItems(object o, EventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			hashErrors=0;

			foreach (ListViewItem lvi in listView.Items)
			{
				string partialpath=lvi.SubItems[COLUMN_PATH].Text;
				string hash=lvi.SubItems[COLUMN_HASH].Text;
				string verified=lvi.SubItems[COLUMN_STATUS].Text;

				if (verified=="Good" || verified=="Loaded")
					continue;

				string dbhash=LookupHash(partialpath);

				if (dbhash!=null)
				{
					if (dbhash==hash)
					{
						verified="Good";
						lvi.BackColor=Color.White;
					}
					else
					{
						verified="BAD";
						lvi.BackColor=Color.Yellow;
						hashErrors++;
					}
				}

				lvi.SubItems[COLUMN_STATUS].Text=verified;
			}

			knownHashesLabel.Text=""+hashDatabase.Count;

			EnableAlert(hashErrors>0);
		}

		protected delegate void SetCurrentlyProcessingDelegate(string s);

		/** Set the currently processing label **/
		protected void SetCurrentlyProcessing(string s)
		{
			Debug.Assert(this.InvokeRequired==false);

			this.currentlyProcessingLabel.Text=s;
		}

		protected delegate void UpdateEnqueuedLabelDelegate(int v);
		protected void UpdateEnqueuedLabel(int v)
		{
			Debug.Assert(this.InvokeRequired==false);

			if (v==1)
				this.enqueuedLabel.Text="(1 item enqueued)";
			else
				this.enqueuedLabel.Text="("+v+" items enqueued)";
		}

		/** The main function of our background thread which actually computes
		 * md5 hashes and loads MD5 sum files.
		 */
		protected void WorkerThreadProc()
		{
			while(!quitting)
			{
				QueueItem item=(QueueItem) queue.Get();
                if (!this.Disposing)
                {
                    try
                    {
                        this.Invoke(new UpdateEnqueuedLabelDelegate(UpdateEnqueuedLabel), new object[1] { queue.Count() });
                    }
                    catch (Exception ex)
                    {
                    }
                }

				if (item==null)
					return;

				WorkerCompute(item);
			}
		}

		/** Test this path name; is it an MD5SUM file? (i.e., should we try to
		 * interpret its contents as MD5 hash data?
		 */
		protected bool IsMD5File(string path)
		{
			if (path.EndsWith(".md5"))
				return true;

			if (JustTheFileName(path).StartsWith("MD5SUM"))
				return true;
			
			return false;
		}

		/** Called by our worker thread, this function handles a single item in the queue. */
		protected void WorkerCompute(QueueItem item)
		{
			// is it a directory?
			if (Directory.Exists(item.fullpath))
			{
				string[] files=Directory.GetFileSystemEntries(item.fullpath);
				foreach (string f in files)
				{
					//QueueItem(f,item.partialpath+"\\"+JustTheFileName(f));
					Enqueue(new QueueItem(f,item.partialpath+"\\"+JustTheFileName(f)));
					this.Invoke(new UpdateEnqueuedLabelDelegate(UpdateEnqueuedLabel),new object[1] { queue.Count()});
				}
				return;
			}

		//	Console.WriteLine(item.partialpath);

			// it's not a directory. Is it a md5 file?
			if (IsMD5File(item.fullpath))
			{
				//Console.WriteLine("caught md5 file");
				StreamReader sr=new StreamReader(item.fullpath);
				string s;
				
				// read each lkine. If the line looks like an md5 hash, add it
				// to the database.
				while ((s=sr.ReadLine())!=null)
				{
					Match m=Regex.Match(s,@"^([0-9a-fA-F]{32})\s+(.+)$",RegexOptions.None);
					if (m.Success && m.Groups.Count==3)
					{
						string p=m.Groups[2].Value.Trim();
						hashDatabase[p.Replace("/","\\")]=m.Groups[1].Value.Trim().ToLower();
					}
				}

				sr.Close();
				listView.Invoke(new EventHandler(this.ReverifyAllItems));
			
				// don't return; we also compute the hash of the md5sum file. (why not?)
			}

			// compute the md5 hash
			FileStream fsr=null;
			try 
			{
				currentlyProcessingLabel.Invoke(new SetCurrentlyProcessingDelegate(SetCurrentlyProcessing), new object[] { item.partialpath });

				fsr=new FileStream(item.fullpath, FileMode.Open, FileAccess.Read);	
				item.size=fsr.Length;

				// wrap the file system's stream in our progress stream. The progress stream
				// updates the thermometer/progress bar as the file is read.
				progStream=new ProgressStream(fsr,progressBar);

				// compute the hash
				// Is it just me, or is this MD5 routine slow?
				System.Security.Cryptography.MD5 md5=new System.Security.Cryptography.MD5CryptoServiceProvider();
				md5.Initialize();
				byte[] hash=md5.ComputeHash(progStream);
				progStream=null;
	
				// we're done. Add the data to the screen
				listView.Invoke(new AddFileToGridDelegate(AddFileToGrid),new object[] {item, ByteArrayToHexadecimalString(hash)});

				md5.Clear();
			}
			catch (Exception e)
			{
				// did they click the abort button?
				if (e.Message.Equals("aborted"))
				{
					queue.Clear();
					this.Invoke(new UpdateEnqueuedLabelDelegate(UpdateEnqueuedLabel),new object[1] { queue.Count()});
				}
				else if (!quitting)
					ReportError("Couldn't process "+item.fullpath+"\r\n\r\nIs it open by another application?");
				return;
			}
			finally
			{
				currentlyProcessingLabel.Invoke(new SetCurrentlyProcessingDelegate(SetCurrentlyProcessing), new object[] { "(idle)" });
				if (fsr!=null)
					fsr.Close();
			}
		}

		protected void ReportError(string s)
		{
			Console.WriteLine(s);
			MessageBox.Show(s,"Error",MessageBoxButtons.OK,MessageBoxIcon.Exclamation);
		}

		/** Convert a byte array into a hexadecimal string... used for MD5 hash data. **/
		protected string ByteArrayToHexadecimalString(byte[] b)
		{
			StringBuilder sb=new StringBuilder();

			for (int i=0;i<b.Length;i++)
			{
				sb.Append(NibbleToHex((b[i]&0x00f0)>>4));
				sb.Append(NibbleToHex(b[i]&0x000f));
			}

			return sb.ToString();
		}

		protected char NibbleToHex(int v)
		{
			if (v<10)
				return (char) ('0'+v);
			else
				return (char) ('a'+(v-10));
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if (components != null) 
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		#region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WinMD5Form));
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.label1 = new System.Windows.Forms.Label();
            this.mainMenu = new System.Windows.Forms.MainMenu(this.components);
            this.menuItem1 = new System.Windows.Forms.MenuItem();
            this.openFileItem = new System.Windows.Forms.MenuItem();
            this.openMD5Item = new System.Windows.Forms.MenuItem();
            this.menuItem5 = new System.Windows.Forms.MenuItem();
            this.saveMD5Item = new System.Windows.Forms.MenuItem();
            this.menuItem6 = new System.Windows.Forms.MenuItem();
            this.quitItem = new System.Windows.Forms.MenuItem();
            this.menuItem2 = new System.Windows.Forms.MenuItem();
            this.copyItem = new System.Windows.Forms.MenuItem();
            this.menuItem3 = new System.Windows.Forms.MenuItem();
            this.alwaysOnTopItem = new System.Windows.Forms.MenuItem();
            this.menuItem7 = new System.Windows.Forms.MenuItem();
            this.aboutItem = new System.Windows.Forms.MenuItem();
            this.webLink = new System.Windows.Forms.LinkLabel();
            this.currentlyProcessingLabel = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.listView = new System.Windows.Forms.ListView();
            this.pathHeader = new System.Windows.Forms.ColumnHeader();
            this.hashHeader = new System.Windows.Forms.ColumnHeader();
            this.sizeHeader = new System.Windows.Forms.ColumnHeader();
            this.verifiedHeader = new System.Windows.Forms.ColumnHeader();
            this.label4 = new System.Windows.Forms.Label();
            this.knownHashesLabel = new System.Windows.Forms.Label();
            this.clearButton = new System.Windows.Forms.Button();
            this.openFileDialog = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.pictureBox = new System.Windows.Forms.PictureBox();
            this.alertTimer = new System.Windows.Forms.Timer(this.components);
            this.alertLabel = new System.Windows.Forms.Label();
            this.enqueuedLabel = new System.Windows.Forms.Label();
            this.abortButton = new System.Windows.Forms.Button();
            this.useCRLF = new System.Windows.Forms.MenuItem();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(8, 50);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(556, 8);
            this.progressBar.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(8, 12);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(120, 20);
            this.label1.TabIndex = 2;
            this.label1.Text = "Currently Processing:";
            // 
            // mainMenu
            // 
            this.mainMenu.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.menuItem1,
            this.menuItem2,
            this.menuItem3,
            this.menuItem7});
            // 
            // menuItem1
            // 
            this.menuItem1.Index = 0;
            this.menuItem1.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.openFileItem,
            this.openMD5Item,
            this.menuItem5,
            this.saveMD5Item,
            this.menuItem6,
            this.quitItem});
            this.menuItem1.Text = "&File";
            // 
            // openFileItem
            // 
            this.openFileItem.Index = 0;
            this.openFileItem.Text = "&Open File...";
            this.openFileItem.Click += new System.EventHandler(this.openFileItem_Click);
            // 
            // openMD5Item
            // 
            this.openMD5Item.Index = 1;
            this.openMD5Item.Text = "Open &MD5 File...";
            this.openMD5Item.Click += new System.EventHandler(this.openMD5Item_Click);
            // 
            // menuItem5
            // 
            this.menuItem5.Index = 2;
            this.menuItem5.Text = "-";
            // 
            // saveMD5Item
            // 
            this.saveMD5Item.Index = 3;
            this.saveMD5Item.Text = "&Save MD5 File...";
            this.saveMD5Item.Click += new System.EventHandler(this.saveMD5Item_Click);
            // 
            // menuItem6
            // 
            this.menuItem6.Index = 4;
            this.menuItem6.Text = "-";
            // 
            // quitItem
            // 
            this.quitItem.Index = 5;
            this.quitItem.Shortcut = System.Windows.Forms.Shortcut.CtrlQ;
            this.quitItem.Text = "&Quit";
            this.quitItem.Click += new System.EventHandler(this.quitItem_Click);
            // 
            // menuItem2
            // 
            this.menuItem2.Index = 1;
            this.menuItem2.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.copyItem});
            this.menuItem2.Text = "&Edit";
            // 
            // copyItem
            // 
            this.copyItem.Index = 0;
            this.copyItem.Shortcut = System.Windows.Forms.Shortcut.CtrlC;
            this.copyItem.Text = "&Copy";
            this.copyItem.Click += new System.EventHandler(this.copyItem_Click);
            // 
            // menuItem3
            // 
            this.menuItem3.Index = 2;
            this.menuItem3.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.alwaysOnTopItem,
            this.useCRLF});
            this.menuItem3.Text = "&Options";
            // 
            // alwaysOnTopItem
            // 
            this.alwaysOnTopItem.Index = 0;
            this.alwaysOnTopItem.Text = "Always on &top";
            this.alwaysOnTopItem.Click += new System.EventHandler(this.alwaysOnTopItem_Click);
            // 
            // menuItem7
            // 
            this.menuItem7.Index = 3;
            this.menuItem7.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.aboutItem});
            this.menuItem7.Text = "&Help";
            // 
            // aboutItem
            // 
            this.aboutItem.Index = 0;
            this.aboutItem.Text = "About WinMD5...";
            this.aboutItem.Click += new System.EventHandler(this.aboutItem_Click);
            // 
            // webLink
            // 
            this.webLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.webLink.Location = new System.Drawing.Point(394, 272);
            this.webLink.Name = "webLink";
            this.webLink.Size = new System.Drawing.Size(184, 23);
            this.webLink.TabIndex = 3;
            this.webLink.TabStop = true;
            this.webLink.Text = "http://www.blisstonia.com/software";
            this.webLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.webLink_LinkClicked);
            // 
            // currentlyProcessingLabel
            // 
            this.currentlyProcessingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.currentlyProcessingLabel.Location = new System.Drawing.Point(120, 12);
            this.currentlyProcessingLabel.Name = "currentlyProcessingLabel";
            this.currentlyProcessingLabel.Size = new System.Drawing.Size(284, 20);
            this.currentlyProcessingLabel.TabIndex = 5;
            this.currentlyProcessingLabel.Text = "(idle)";
            // 
            // label3
            // 
            this.label3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label3.Location = new System.Drawing.Point(8, 272);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(304, 16);
            this.label3.TabIndex = 6;
            this.label3.Text = "Drag files and MD5SUM files (if available) into this window.";
            // 
            // listView
            // 
            this.listView.AllowColumnReorder = true;
            this.listView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.listView.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.listView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.pathHeader,
            this.hashHeader,
            this.sizeHeader,
            this.verifiedHeader});
            this.listView.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listView.FullRowSelect = true;
            this.listView.GridLines = true;
            this.listView.ImeMode = System.Windows.Forms.ImeMode.On;
            this.listView.Location = new System.Drawing.Point(8, 64);
            this.listView.Name = "listView";
            this.listView.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.listView.Size = new System.Drawing.Size(556, 168);
            this.listView.TabIndex = 7;
            this.listView.UseCompatibleStateImageBehavior = false;
            this.listView.View = System.Windows.Forms.View.Details;
            this.listView.Resize += new System.EventHandler(this.listView_Resize);
            this.listView.ColumnClick += new System.Windows.Forms.ColumnClickEventHandler(this.listView_ColumnClick);
            // 
            // pathHeader
            // 
            this.pathHeader.Text = "Path";
            this.pathHeader.Width = 108;
            // 
            // hashHeader
            // 
            this.hashHeader.Text = "Hash";
            this.hashHeader.Width = 246;
            // 
            // sizeHeader
            // 
            this.sizeHeader.Text = "Bytes";
            // 
            // verifiedHeader
            // 
            this.verifiedHeader.Text = "Status";
            this.verifiedHeader.Width = 71;
            // 
            // label4
            // 
            this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.label4.Location = new System.Drawing.Point(250, 248);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(288, 16);
            this.label4.TabIndex = 8;
            this.label4.Text = "Number of known md5 hashes found in MD5SUM files: ";
            // 
            // knownHashesLabel
            // 
            this.knownHashesLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.knownHashesLabel.Location = new System.Drawing.Point(530, 248);
            this.knownHashesLabel.Name = "knownHashesLabel";
            this.knownHashesLabel.Size = new System.Drawing.Size(32, 23);
            this.knownHashesLabel.TabIndex = 9;
            this.knownHashesLabel.Text = "0";
            this.knownHashesLabel.TextAlign = System.Drawing.ContentAlignment.TopRight;
            // 
            // clearButton
            // 
            this.clearButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.clearButton.Location = new System.Drawing.Point(8, 240);
            this.clearButton.Name = "clearButton";
            this.clearButton.Size = new System.Drawing.Size(75, 23);
            this.clearButton.TabIndex = 10;
            this.clearButton.Text = "&Clear";
            this.clearButton.Click += new System.EventHandler(this.clearButton_Click);
            // 
            // openFileDialog
            // 
            this.openFileDialog.Multiselect = true;
            // 
            // pictureBox
            // 
            this.pictureBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox.Image")));
            this.pictureBox.InitialImage = ((System.Drawing.Image)(resources.GetObject("pictureBox.InitialImage")));
            this.pictureBox.Location = new System.Drawing.Point(521, 4);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new System.Drawing.Size(35, 40);
            this.pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox.TabIndex = 11;
            this.pictureBox.TabStop = false;
            this.pictureBox.Visible = false;
            this.pictureBox.Click += new System.EventHandler(this.pictureBox_Click);
            // 
            // alertTimer
            // 
            this.alertTimer.Interval = 400;
            this.alertTimer.Tick += new System.EventHandler(this.alertTimer_Tick);
            // 
            // alertLabel
            // 
            this.alertLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.alertLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.alertLabel.Location = new System.Drawing.Point(368, 15);
            this.alertLabel.Name = "alertLabel";
            this.alertLabel.Size = new System.Drawing.Size(147, 17);
            this.alertLabel.TabIndex = 12;
            this.alertLabel.Text = "Errors Found";
            this.alertLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.alertLabel.Visible = false;
            this.alertLabel.Click += new System.EventHandler(this.alertLabel_Click);
            // 
            // enqueuedLabel
            // 
            this.enqueuedLabel.Location = new System.Drawing.Point(7, 30);
            this.enqueuedLabel.Name = "enqueuedLabel";
            this.enqueuedLabel.Size = new System.Drawing.Size(265, 16);
            this.enqueuedLabel.TabIndex = 13;
            this.enqueuedLabel.Text = "(0 files enqueued)";
            // 
            // abortButton
            // 
            this.abortButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.abortButton.Location = new System.Drawing.Point(104, 240);
            this.abortButton.Name = "abortButton";
            this.abortButton.Size = new System.Drawing.Size(75, 23);
            this.abortButton.TabIndex = 14;
            this.abortButton.Text = "&Abort";
            this.abortButton.Click += new System.EventHandler(this.abortButton_Click);
            // 
            // useCRLF
            // 
            this.useCRLF.Checked = true;
            this.useCRLF.Index = 1;
            this.useCRLF.Text = "Use &CRLF when saving";
            this.useCRLF.Click += new System.EventHandler(this.useCRLF_Click);
            // 
            // WinMD5Form
            // 
            this.AllowDrop = true;
            this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            this.ClientSize = new System.Drawing.Size(572, 297);
            this.Controls.Add(this.abortButton);
            this.Controls.Add(this.enqueuedLabel);
            this.Controls.Add(this.alertLabel);
            this.Controls.Add(this.pictureBox);
            this.Controls.Add(this.clearButton);
            this.Controls.Add(this.knownHashesLabel);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.listView);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.currentlyProcessingLabel);
            this.Controls.Add(this.webLink);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.progressBar);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Menu = this.mainMenu;
            this.MinimumSize = new System.Drawing.Size(580, 208);
            this.Name = "WinMD5Form";
            this.Text = "Title will be set at runtime in WinMD5Form constructor";
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.WinMD5Form_DragDrop);
            this.Closing += new System.ComponentModel.CancelEventHandler(this.WinMD5Form_Closing);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.WinMD5Form_DragEnter);
            this.Load += new System.EventHandler(this.WinMD5Form_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();
            this.ResumeLayout(false);

		}
		#endregion

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args) 
		{
			WinMD5Form f=new WinMD5Form();

			if (args!=null)
			{
				foreach (string arg in args)
				{
					f.Enqueue(new QueueItem(arg,WinMD5Form.JustTheFileName(arg)));
				}
			}

			Application.Run(f);
		}

		private void WinMD5Form_DragDrop(object sender, System.Windows.Forms.DragEventArgs e)
		{
			if(e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				// Assign the file names to a string array, in 
				// case the user has selected multiple files.
				string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

				foreach (string file in files)
				{
					int lastdelim=file.LastIndexOf("\\");
	
					Enqueue(new QueueItem(file,JustTheFileName(file)));
				}
			}
		}

		/** Return just the file name, removing any directory information. **/
		public static string JustTheFileName(string path)
		{
			int lastdelim=path.LastIndexOf("\\");
			string filename;

			if (lastdelim>=0)
				filename=path.Substring(lastdelim+1);
			else
				filename=path;

			return filename;
		}

		// Add an item to the queue.
		protected void Enqueue(QueueItem item)
		{
	//		Console.WriteLine(item);
			queue.Put(item);
		}

		private void WinMD5Form_DragEnter(object sender, System.Windows.Forms.DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect=DragDropEffects.Copy;
			else
				e.Effect=DragDropEffects.None;
		}

		/** We try to be very smart about handling resizing of the window, to show the most useful
		 * information possible. Our goal: The user never needs to individually adjust the width of a column.
		 */
		private void listView_Resize(object sender, System.EventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

		//	return;

			Bitmap bmap=new Bitmap(16,16);
			Graphics g=Graphics.FromImage(bmap);

			// Measure how wide (worst case) our fields could be.
			SizeF hashSize=g.MeasureString("88888888888888888888888888888888888888",listView.Font);
			SizeF verfSize=g.MeasureString("VerifiedWW",listView.Font);
			SizeF sizeSize=g.MeasureString("88888888888",listView.Font);

			listView.Columns[COLUMN_STATUS].Width=(int) verfSize.Width;
			listView.Columns[COLUMN_SIZE].Width=(int) sizeSize.Width;
			listView.Columns[COLUMN_HASH].Width=(int) hashSize.Width;
			listView.Columns[COLUMN_PATH].Width=Math.Max(100,listView.DisplayRectangle.Width
				- listView.Columns[COLUMN_HASH].Width
				- listView.Columns[COLUMN_SIZE].Width
				- listView.Columns[COLUMN_STATUS].Width);
		}

		/** Handle sorting by column type. */
		private void listView_ColumnClick(object sender, System.Windows.Forms.ColumnClickEventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			if (e.Column==reverseSortLastColumn)
				reverseSort=!reverseSort;
			else
			{
				reverseSort=false;
				reverseSortLastColumn=e.Column;
			}

			listView.ListViewItemSorter=new ListViewItemComparer(e.Column, reverseSort);
			listView.Sort();
			listView.Sorting=SortOrder.None;
		}

		/** Clear all state, including MD5 database.  */
		private void clearButton_Click(object sender, System.EventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			listView.Items.Clear();
			hashDatabase.Clear();
			knownHashesLabel.Text="0";
			hashErrors=0;
			EnableAlert(false);
		}

		private void abortProcessing()
		{
			if (progStream!=null)
				progStream.AbortRead();
		}

		private void quitItem_Click(object sender, System.EventArgs e)
		{
			quitting=true;
			abortProcessing();
			queue.Put(null);
			Application.Exit();
		}

		private void WinMD5Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			quitting=true;
			abortProcessing();
			queue.Put(null);
			Application.Exit();		
		}

		private void webLink_LinkClicked(object sender, System.Windows.Forms.LinkLabelLinkClickedEventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			System.Diagnostics.Process.Start(webLink.Text);
		}

		private void openFileItem_Click(object sender, System.EventArgs e)
		{
			openFileDialog.Filter="All Files (*.*) | *.*";

			DialogResult res=openFileDialog.ShowDialog();
			if (res!=DialogResult.OK)
				return;

			foreach (string f in openFileDialog.FileNames)
				this.Enqueue(new QueueItem(f, JustTheFileName(f)));
		
		}

		private void openMD5Item_Click(object sender, System.EventArgs e)
		{
			openFileDialog.Filter=(" MD5Files (MD5SUM*.*, *.md5) | *.md5;MD5SUM*.*;MD5SUM | All Files | *.*");
	//		openFileDialog.Filter=" MD5 Files (*.md5) | *.md5 | MD5SUM Files | MD5SUM*.* | All Files (*.*) | *.*";
	//		openFileDialog.FileName="*";
			
			DialogResult res=openFileDialog.ShowDialog();
			if (res!=DialogResult.OK)
				return;

			foreach (string f in openFileDialog.FileNames)
				this.Enqueue(new QueueItem(f, JustTheFileName(f)));
		}

		private void saveMD5Item_Click(object sender, System.EventArgs e)
		{
			saveFileDialog.Filter="MD5 without directory info (*.md5) | *.md5 | MD5 with directory info (*.md5) | *.md5 ";
			saveFileDialog.FileName="MD5SUM";

			DialogResult res=saveFileDialog.ShowDialog();
			if (res!=DialogResult.OK)
				return;

			StreamWriter sw=new StreamWriter(saveFileDialog.FileName);
			foreach (ListViewItem lvi in listView.Items)
			{
				string partialpath=lvi.SubItems[0].Text;
				string hash=lvi.SubItems[1].Text;
				string verified=lvi.SubItems[2].Text;

				string p;
				if (saveFileDialog.FilterIndex==1)
					p=JustTheFileName(partialpath);
				else
					p=partialpath;

                sw.Write(hash + "  " + p);

                if (useCRLF.Checked)
                    sw.Write("\r\n");
                else
                    sw.Write("\n");
			}

			sw.Close();
		}

		private void aboutItem_Click(object sender, System.EventArgs e)
		{
			MessageBox.Show("WinMD5 "+version+", (C) 2003-2004 by Edwin Olson (eolson@mit.edu)\r\n\r\nFor more information, please visit our website:\r\nhttp://www.blisstonia.com/software\r\n\r\nYou may freely distribute this application, but you may not charge for it.","About",MessageBoxButtons.OK,MessageBoxIcon.None);

		}

		private void alertTimer_Tick(object sender, System.EventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			pictureBox.Visible=!pictureBox.Visible;
		}

		private void alertLabel_Click(object sender, System.EventArgs e)
		{
			ShowErrors();
		}

		private void pictureBox_Click(object sender, System.EventArgs e)
		{
			ShowErrors();
		}

		private void ShowErrors()
		{
			Debug.Assert(this.InvokeRequired==false);

			reverseSort=false;
			reverseSortLastColumn=COLUMN_STATUS;

			listView.ListViewItemSorter=new ListViewItemComparer(COLUMN_STATUS, false);
			listView.Sort();
			listView.Sorting=SortOrder.None;
			//listView.EnsureVisible(0);
		}

		/** Copy an MD5SUM-style text buffer to the clipboard for the selected items. **/
		private void copyItem_Click(object sender, System.EventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			string data="";
			foreach (ListViewItem lvi in listView.SelectedItems)
			{
				string partialpath=lvi.SubItems[COLUMN_PATH].Text;
				string hash=lvi.SubItems[COLUMN_HASH].Text;
				string verified=lvi.SubItems[COLUMN_STATUS].Text;
				string p=JustTheFileName(partialpath);

						
				data+=hash+"  "+p+"\r\n";

				Clipboard.SetDataObject(data, true);
			}
		}

		private void abortButton_Click(object sender, System.EventArgs e)
		{
			Debug.Assert(this.InvokeRequired==false);

			if (progStream!=null)
				progStream.AbortRead();
		}

		// Implements the manual sorting of items by columns.
		class ListViewItemComparer : IComparer 
		{
			private int col;
			private bool reverseSort;

			public ListViewItemComparer(int column, bool reverseSort) 
			{
				col=column;
				this.reverseSort=reverseSort;
			}
			public int Compare(object x, object y) 
			{
				if (col==COLUMN_PATH || col==COLUMN_STATUS)
				{	
					string a=((ListViewItem)x).SubItems[col].Text;
					string b=((ListViewItem)y).SubItems[col].Text;
	
					if (reverseSort)
						return a.CompareTo(b);
					else
						return b.CompareTo(a);
				}
				if (col==COLUMN_HASH)
				{
					int a=int.Parse(((ListViewItem)x).SubItems[col].Text.Substring(0,7), System.Globalization.NumberStyles.HexNumber);
					int b=int.Parse(((ListViewItem)y).SubItems[col].Text.Substring(0,7), System.Globalization.NumberStyles.HexNumber);
				
					if (reverseSort)
						
						return a-b;
					else
						return b-a;
				}

				if (col==COLUMN_SIZE)
				{
					int a=int.Parse(((ListViewItem)x).SubItems[col].Text);
					int b=int.Parse(((ListViewItem)y).SubItems[col].Text);
					if (reverseSort)
						return a-b;
					else
						return b-a;
				}
				return 0;
			}
		}

        private void WinMD5Form_Load(object sender, EventArgs e)
        {

        }

        private void alwaysOnTopItem_Click(object sender, EventArgs e)
        {
            alwaysOnTopItem.Checked ^= true;

            this.TopMost = alwaysOnTopItem.Checked;
        }

        private void useCRLF_Click(object sender, EventArgs e)
        {
            useCRLF.Checked ^= true;
        }


	}
}

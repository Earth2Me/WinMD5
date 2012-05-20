using System;
using System.IO;
using System.Windows.Forms;

namespace WinMD5
{
	/// <summary>
	/// Summary description for ProgressStream.
	/// </summary>
	public class ProgressStream : Stream
	{
		public delegate void ProgressUpdateEvent(long v, long m);

		Stream fs;
		long readcount=0;
		long readnotifycount;
		
		long fileLength=0;

		static long fileLengthDivider=1024;
		static long numNotifications=200;

		ProgressBar progressBar;
		bool abort=false;

		public ProgressStream(Stream fs, ProgressBar progressBar)
		{
			this.fs=fs;
		//	this.readnotifycount=readnotifycount;
			this.progressBar=progressBar;
			this.fileLength=fs.Length;
			this.readnotifycount=fileLength/fileLengthDivider/numNotifications;

			progressBar.Invoke(new EventHandler(SetMaximumPosition));
		}

		protected void SetMaximumPosition(object o, EventArgs e)
		{
			lock(this)
			{
				System.Diagnostics.Debug.Assert(progressBar.InvokeRequired==false);
				progressBar.Maximum=(int) (1+fileLength/fileLengthDivider);
			}
		}

		public override bool CanRead
		{
			get 
			{
				lock(this)
				{
					return fs.CanRead;
				}
			}
		}

		public override bool CanSeek
		{
			get 
			{
				lock(this)
				{
					return fs.CanSeek;
				}
			}
		}

		public override bool CanWrite
		{
			get 
			{
				lock(this)
				{
					return fs.CanWrite;
				}
			}

		}

		public override long Length
		{
			get 
			{
				lock(this)
				{
					return fileLength;
				}
			}

		}

		public override long Position
		{
			get 
			{
				lock(this) 
				{
					return fs.Position;
				}
			}
			set
			{
				lock(this)
				{
					fs.Position=value;
				}
			}
		}

		public override void Flush()
		{
			lock(this)
			{
				fs.Flush();
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			lock(this)
			{
				if (abort)
					throw new IOException("aborted");

				int len=fs.Read(buffer,offset,count);
			
				readcount+=len;
				if (readcount>readnotifycount)
				{
					readcount=0;
					//	readcount-=readnotifycount;
				//	if (ProgressUpdate!=null)
				//		ProgressUpdate(this.Position, this.Length);

					if (progressBar!=null)
					{
						progressBar.BeginInvoke(new ProgressUpdateEvent(ProgressBarUpdateProc), new object[] {0,0});
					}
				}
				return len;
			}
		}

		void ProgressBarUpdateProc(long v, long m)
		{
			lock(this)
			{
				System.Diagnostics.Debug.Assert(progressBar.InvokeRequired==false);
	
				long pos=this.Position;

				if (pos==fileLength)
					progressBar.Value=progressBar.Maximum;
				else
					progressBar.Value=(int) (pos/fileLengthDivider);
			}
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			lock(this)
			{
				return fs.Seek(offset, origin);
			}
		}

		public override void SetLength(long value)
		{
			lock(this)
			{
				fs.SetLength(value);
			}
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			lock(this)
			{
				fs.Write(buffer, offset, count);
			}
		}

		public override void Close()
		{
			lock(this)
			{
				fs.Close();
				base.Close();
			}
		}

		public void AbortRead()
		{
			lock(this)
			{
				abort=true;
			}
		}
	}
}

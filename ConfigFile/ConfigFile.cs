using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace ConfigFile
{
	/// <summary>
	/// Read text based configuration file based on key value pairs.
	/// </summary>
	public class ConfigReader
	{
		/// <summary>
		/// Hold the key value pairs.
		/// </summary>
		protected Hashtable _configValues = new Hashtable(StringComparer.OrdinalIgnoreCase);
		
		/// <summary>
		/// Occurs before a new key is added.
		/// </summary>
		public event EventHandler<ConfigReaderEventArgs> OnKeyAdd;

		/// <summary>
		/// Occurs before a existing key is overwritten.
		/// </summary>
		public event EventHandler<ConfigReaderEventArgs> OnKeyChange;

		/// <summary>
		/// Hold lines for here mode.
		/// </summary>
		private List<string> _here = new List<string>();

		/// <summary>
		/// Initialize a new instance.
		/// </summary>
		public ConfigReader()
		{ }

		/// <summary>
		/// Initialize new instance and read the configuration from file.
		/// </summary>
		/// <param name="filename"></param>
		public ConfigReader(string filename)
		{
			if (String.IsNullOrEmpty(filename))
			{
				throw new ArgumentNullException(filename);
			}
			Open(filename);
		}

		/// <summary>
		/// Initialize new instance and read from stream.
		/// </summary>
		/// <param name="stream"></param>
		public ConfigReader(Stream stream)
		{
			if (stream == null)
				throw new ArgumentException("stream");

			Open(stream);
		}

		/// <summary>
		/// Open configuration file and clear existing values.
		/// </summary>
		/// <param name="filename"></param>
		public void Open(string filename)
		{
			if (!File.Exists(filename))
				throw new FileNotFoundException("File not found.", filename);

			_configValues.Clear();

			Parse(() => File.OpenRead(filename));
		}

		/// <summary>
		/// Read stream and clear existing values.
		/// </summary>
		/// <param name="stream"></param>
		public void Open(Stream stream)
		{
			if (stream == null)
				throw new ArgumentException("stream");

			_configValues.Clear();

			using (var ms = new MemoryStream())
			{
				stream.CopyTo(ms);
				ms.Position = 0;

				Parse(() => ms);
			}
		}

		/// <summary>
		/// Open configuration file and add values.
		/// </summary>
		/// <param name="filename"></param>
		public void Append(string filename)
		{
			if (!File.Exists(filename))
				throw new FileNotFoundException("File not found.", filename);

			Parse(() => File.OpenRead(filename));
		}

		/// <summary>
		/// Read stream and add values.
		/// </summary>
		/// <param name="stream"></param>
		public void Append(Stream stream)
		{
			if (stream == null)
				throw new ArgumentException("stream");

			using (var ms = new MemoryStream())
			{
				stream.CopyTo(ms);
				ms.Position = 0;

				Parse(() => ms);
			}
		}

		/// <summary>
		/// Read all lines from stream.
		/// </summary>
		/// <param name="streamProvider"></param>
		/// <returns></returns>
		private IEnumerable<string> ReadAllLines(Func<Stream> streamProvider)
		{
			using (var stream = streamProvider())
			{
				using (var reader = new StreamReader(stream))
				{
					while (!reader.EndOfStream)
					{
						yield return reader.ReadLine();
					}
				}
			}
		}

		/// <summary>
		/// Read a stream and parse.
		/// </summary>
		/// <param name="stream">The stream.</param>
		private void Parse(Func<Stream> stream)
		{
			var linenumber = 0;
			var key = "";
			var value = "";
			var mode = ParserMode.Normal;
			var stop = "";
			var literal = false;

			foreach (var rawline in this.ReadAllLines(stream))
			{
				linenumber++;

				var line = rawline.Trim();

				// Test for comments or empty lines and not here mode
				if ((line.StartsWith("#") || line.StartsWith(";") || line.Length == 0) && mode != ParserMode.Here)
				{
					continue;
				}

				// normal parser mode
				if (mode == ParserMode.Normal)
				{
					// Split to <key> = <value> pair
					var pair = line.Split(new char[] { '=' }, 2);

					// Test for one part
					if (pair.Length == 1)
					{
						throw new ConfigException(String.Format("Not a key value pair. Line {0}.", linenumber));
					}

					// Test for two parts
					if (pair.Length == 2)
					{
						key = pair[0].Trim();
						value = pair[1].Trim();

						if (String.IsNullOrEmpty(key))
						{
							throw new ConfigException(String.Format("The key is null. Line {0}.", linenumber));
						}

						// test for valid key format
						if (!Regex.IsMatch(key, "^@?[a-zA-Z_][a-zA-Z0-9_.]*$", RegexOptions.Singleline))
						{
							throw new ConfigException(String.Format("The format for key '{0}' is invalid. Line {1}.", key, linenumber));
						}

						// literal mode
						if (key.StartsWith("@"))
						{
							key = key.Substring(1);
							literal = true;
						}
					}

					// Test for here document mode sequence
					var inline = Regex.Match(value, "^<<([a-zA-Z][a-zA-Z0-9]*)$");
					if (inline.Success)
					{
						// test for literal
						if (literal)
							throw new ConfigException("Literal mode in here document not allowed.");
						
						// stopword
						stop = inline.Groups[1].Value;

						_here.Clear();

						mode = ParserMode.Here;
						continue;
					}
				}

				// Test, if in continuation mode
				if (mode == ParserMode.Continuation)
				{
					value = String.Concat(value, line);
				}

				// Test, if in here document mode
				if (mode == ParserMode.Here)
				{
					// Test for stopword.
					if (rawline.Equals(stop))
					{
#if NET35
						Add(key, String.Join("\n", _here.ToArray()));
#else
						Add(key, String.Join("\n", _here));
#endif
						
						// reset parser
						literal = false;
						mode = ParserMode.Normal;

						_here.Clear();

						continue;
					}

					_here.Add(rawline);
					continue;
				}

				// Test for trailing backslash (continuation mode)
				if (value.EndsWith("\\"))
				{
					value = value.Substring(0, value.Length - 1);
					
					mode = ParserMode.Continuation;
					continue;
				}

				// Test for surrounding double quotes
				if (value.StartsWith("\"") && value.EndsWith("\""))
				{
					value = value.Substring(1, value.Length - 2);
				}

				if (!literal)
				{
					// Unescape the value
					value = Regex.Replace(value, @"(\\(.?))", m =>
					{
						switch (m.Groups[2].Value)
						{
							case "n":
								return "\n";

							case "t":
								return "\t";

							case "\\":
								return "\\";

							case "":
								throw new ConfigException(String.Format("Empty escape sequence. Line {0}.", linenumber));

							default:
								throw new ConfigException(String.Format("Unknown escape sequence: \\{0}. Line {1}.", m.Groups[2].Value, linenumber));
						}
					});
				}

				Add(key, value);

				// reset parser
				literal = false;
				mode = ParserMode.Normal;
			}

			if (mode != ParserMode.Normal)
			{
				throw new ConfigException(String.Format("End of file encountered in {0} mode.", mode.ToString().ToLower()));
			}
		}

		/// <summary>
		/// Add the key and value to storage or override existing.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		private void Add(string key, string value)
		{
			var e = new ConfigReaderEventArgs(key, value);
			if (!_configValues.ContainsKey(key))
			{
				if (OnKeyAdd != null)
					OnKeyAdd(this, e);

				if (!e.Decline)
					_configValues.Add(key, value);
			}
			else
			{
				if (OnKeyChange != null)
					OnKeyChange(this, e);

				if (!e.Decline)
					_configValues[key] = value;
			}
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// If the key does not exists, the default value is returned.
		/// </summary>
		/// <typeparam name="T">Destination type.</typeparam>
		/// <param name="key">The key of the value to get.</param>
		/// <param name="defaultValue">The default value if key not found.</param>
		/// <param name="valueNotEmpty">If the value is empty, then retrun the default value.</param>
		/// <returns>The value from the key, otherwise the default value.</returns>
		public T GetValue<T>(string key, T defaultValue, bool valueNotEmpty = false) where T : IConvertible
		{
			return GetValue<T>(key, defaultValue, CultureInfo.InvariantCulture, valueNotEmpty);
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// If the key does not exists, the default value is returned.
		/// </summary>
		/// <typeparam name="T">Destination type.</typeparam>
		/// <param name="key">The key of the value to get.</param>
		/// <param name="defaultValue">The default value if key not found.</param>
		/// <param name="provider">The format provider.</param>
		/// <param name="valueNotEmpty">If the value is empty, then retrun the default value.</param>
		/// <returns>The value from the key, otherwise the default value.</returns>
		public T GetValue<T>(string key, T defaultValue, IFormatProvider provider, bool valueNotEmpty = false) where T : IConvertible
		{
			if (String.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

			if (_configValues.ContainsKey(key))
			{
				var _value = _configValues[key] as string;
				if (valueNotEmpty && String.IsNullOrEmpty(_value))
				{
					return defaultValue;
				}

				try
				{
					return (T)Convert.ChangeType(_value, typeof(T), provider);
				}
				catch (FormatException e)
				{
					throw new ConfigException(String.Format("Can't convert key '{0}' to destination type '{1}'.", key, typeof(T)), e);
				}
			}
			return defaultValue;
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// If the key does not exists, an exception is thrown.
		/// </summary>
		/// <typeparam name="T">Destination type.</typeparam>
		/// <param name="key">The key of the value to get.</param>
		/// <param name="valueNotEmpty">If the value is empty, then throw an exception.</param>
		/// <returns>Throws an exception if the key is not found.</returns>
		public T TryGetValue<T>(string key, bool valueNotEmpty = false) where T : IConvertible
		{
			return TryGetValue<T>(key, CultureInfo.InvariantCulture, valueNotEmpty);
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// If the key does not exists, an exception is thrown.
		/// </summary>
		/// <typeparam name="T">Destination type.</typeparam>
		/// <param name="key">The key of the value to get.</param>
		/// <param name="provider">The format provider.</param>
		/// <param name="valueNotEmpty">If the value is empty, then throw an exception.</param>
		/// <returns>Throws a exception if the key is not found.</returns>
		public T TryGetValue<T>(string key, IFormatProvider provider, bool valueNotEmpty = false) where T : IConvertible
		{
			if(String.IsNullOrEmpty(key))
			{
				throw new ArgumentNullException("key");
			}

			if(_configValues.ContainsKey(key))
			{
				var _value = _configValues[key] as string;
				if (valueNotEmpty && String.IsNullOrEmpty(_value))
				{
					throw new ConfigException(String.Format("The value for key '{0}' must not be empty.", key));
				}

				try
				{
					return (T)Convert.ChangeType(_value, typeof(T), provider);
				}
				catch (FormatException e)
				{
					throw new ConfigException(String.Format("Can't convert key '{0}' to destination type '{1}'.", key, typeof(T)), e);
				}
			}
			throw new ConfigException(String.Format("The configuration key '{0}' does not exists.", key));
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <typeparam name="T">The destination type.</typeparam>
		/// <param name="key">The key of the value to get.</param>
		/// <param name="value">The value if the is found, otherwise the default value.</param>
		/// <param name="valueNotEmpty">If the value is empty, then false is returned.</param>
		/// <returns>Returns true if the key is found, otherwise false.</returns>
		public bool TryGetValue<T>(string key, out T value, bool valueNotEmpty = false) where T : IConvertible
		{
			return TryGetValue<T>(key, out value, CultureInfo.InvariantCulture, valueNotEmpty);
		}

		/// <summary>
		/// Gets the value associated with the specified key.
		/// </summary>
		/// <typeparam name="T">The destination type.</typeparam>
		/// <param name="key">The key of the value to get.</param>
		/// <param name="value">The value if the is found, otherwise the default value.</param>
		/// <param name="provider">The format provider.</param>
		/// <param name="valueNotEmpty">If the value is empty, then false is returned.</param>
		/// <returns>Returns true if the key is found, otherwise false.</returns>
		public bool TryGetValue<T>(string key, out T value, IFormatProvider provider, bool valueNotEmpty = false) where T : IConvertible
		{
			value = default(T);

			if (String.IsNullOrEmpty(key))
			{
				throw new ArgumentNullException("key");
			}

			if (_configValues.ContainsKey(key))
			{
				var _value = _configValues[key] as string;
				if (valueNotEmpty && String.IsNullOrEmpty(_value))
				{
					return false;
				}

				try
				{
					value = (T)Convert.ChangeType(_value, typeof(T), provider);
					return true;
				}
				catch (FormatException e)
				{
					throw new ConfigException(String.Format("Can't convert key '{0}' to destination type '{1}'.", key, typeof(T)), e);
				}
			}

			return false;
		}

		/// <summary>
		/// Test if the key exists.
		/// </summary>
		/// <param name="key">The configuration key.</param>
		/// <returns></returns>
		public bool KeyExists(string key)
		{
			if (String.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

			return _configValues.ContainsKey(key);
		}

		/// <summary>
		/// Get all available configuration keys.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<string> GetKeys()
		{
			return _configValues.Keys.Cast<string>();
		}

		/// <summary>
		/// Get all available configuration values.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<string> GetValues()
		{
			return _configValues.Values.Cast<string>();
		}
	}

	/// <summary>
	/// Write text based configuration file based on key value pairs.
	/// </summary>
	public class ConfigWriter
	{
		/// <summary>
		/// Hold the key value pairs.
		/// </summary>
		protected Hashtable _configValues = new Hashtable(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Initialize new configuration writer.
		/// </summary>
		public ConfigWriter()
		{ }

		/// <summary>
		/// Initialize new configuration writer and read in an existing reader.
		/// </summary>
		/// <param name="reader">The configuration reader.</param>
		public ConfigWriter(ConfigReader reader)
			: this(reader, CultureInfo.InvariantCulture)
		{ }

		/// <summary>
		/// Initialize new configuration writer and read in an existing reader.
		/// </summary>
		/// <param name="reader">The configuration reader.</param>
		/// <param name="provider">The format provider.</param>
		public ConfigWriter(ConfigReader reader, IFormatProvider provider)
		{
			if (reader == null)
				throw new ArgumentNullException("reader");

			if (provider == null)
				throw new ArgumentNullException("provider");

			foreach (var key in reader.GetKeys())
			{
				AddValue<string>(key, reader.GetValue<string>(key, ""), provider);
			}
		}

		/// <summary>
		/// Save configuration to file.
		/// </summary>
		/// <param name="filename">The filename.</param>
		public void Save(string filename)
		{
			using (var stream = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				Writer(stream);
			}
		}

		/// <summary>
		/// Save configuration to stream.
		/// </summary>
		/// <param name="stream">The stream.</param>
		public void Save(Stream stream)
		{
			Writer(stream);
		}

		/// <summary>
		/// Write key value pair to stream.
		/// </summary>
		/// <param name="stream">The stream.</param>
		private void Writer(Stream stream)
		{
			var writer = new StreamWriter(stream);

			foreach (var key in _configValues.Keys)
			{
				writer.WriteLine(String.Format("{0} = {1}", key, Prepare((string)_configValues[key])));
			}

			writer.Flush();
		}

		/// <summary>
		/// Add or set a key value pair.
		/// </summary>
		/// <typeparam name="T">Type of value.</typeparam>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void AddValue<T>(string key, T value) where T : IConvertible
		{
			AddValue<T>(key, value, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Add or set a key value pair.
		/// </summary>
		/// <typeparam name="T">Type of value.</typeparam>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		/// <param name="provider">The format provider.</param>
		public void AddValue<T>(string key, T value, IFormatProvider provider) where T : IConvertible
		{
			if (String.IsNullOrEmpty(key))
			{
				throw new ConfigException(String.Format("The key is null or empty."));
			}

			if (!Regex.IsMatch(key, "^[a-zA-Z_][a-zA-Z0-9_.]*$", RegexOptions.Singleline))
			{
				throw new ConfigException(String.Format("The format for key '{0}' is invalid.", key));
			}

			if (!_configValues.ContainsKey(key))
			{
				_configValues.Add(key, String.Format(provider, "{0}", value));
			}
			else
			{
				_configValues[key] = String.Format(provider, "{0}", value);
			}
		}

		/// <summary>
		/// Prepare the value for writing.
		/// </summary>
		/// <param name="input">The value.</param>
		/// <returns>The prepared value.</returns>
		private string Prepare(string input)
		{
			if (Char.IsWhiteSpace(input.FirstOrDefault()) || Char.IsWhiteSpace(input.LastOrDefault()))
			{
				input = String.Format("\"{0}\"", input);
			}

			input = Regex.Replace(input, @"(\n|\t|\\)", m =>
			{
				switch (m.Groups[1].Value)
				{
					case "\n":
						return "\\n";

					case "\t":
						return "\\t";

					case "\\":
						return "\\\\";
				}

				throw new ConfigException(String.Format("Unknown escape sequence: \\{0}.", m.Groups[1].Value));
			});

			return input;
		}
	}

	/// <summary>
	/// ConfigFile event args.
	/// </summary>
	public class ConfigReaderEventArgs : EventArgs
	{
		/// <summary>
		/// Get the key.
		/// </summary>
		public string Key { get; private set; }

		/// <summary>
		/// Get the value.
		/// </summary>
		public string Value { get; private set; }

		/// <summary>
		/// Set to true to decline.
		/// </summary>
		public bool Decline { get; set; }

		/// <summary>
		/// Initialize new event arguments.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		/// <param name="decline">Decline value.</param>
		public ConfigReaderEventArgs(string key, string value, bool decline = false)
		{
			Key = key;
			Value = value;
			Decline = decline;
		}
	}

	/// <summary>
	/// Processing modes of the parser
	/// </summary>
	public enum ParserMode
	{
		/// <summary>
		/// Normal parsing mode.
		/// </summary>
		Normal,

		/// <summary>
		/// Continuation mode.
		/// </summary>
		Continuation,

		/// <summary>
		/// Here document mode.
		/// </summary>
		Here
	}

	/// <summary>
	/// Key mode.
	/// </summary>
	public enum KeyMode
	{
		/// <summary>
		/// Overwrite value of the existing key.
		/// </summary>
		Overwrite,

		/// <summary>
		/// Append value to existing key.
		/// </summary>
		Append,

		/// <summary>
		/// Ignore new value for existing key.
		/// </summary>
		Ignore
	}

	/// <summary>
	/// This exception is thrown when an error in the ConfigFile occurs.
	/// </summary>
	/// <remarks>
	/// This is the base exception for all exceptions thrown in the ConfigFile
	/// </remarks>
	[Serializable]
	public class ConfigException : ApplicationException
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="T:CS.Helper.ConfigFileException" /> class.
		/// </summary>
		public ConfigException()
			: base("ConfigFile caused an exception.")
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:CS.Helper.ConfigFileException" /> class.
		/// </summary>
		public ConfigException(Exception ex)
			: base("ConfigFile caused an exception.", ex)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:CS.Helper.ConfigFileException" /> class.
		/// </summary>
		public ConfigException(string message)
			: base(message)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:CS.Helper.ConfigFileException" /> class.
		/// </summary>
		public ConfigException(string message, Exception innerException)
			: base(message, innerException)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="T:CS.Helper.ConfigFileException" /> class.
		/// </summary>
		protected ConfigException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
		}
	}

#if NET35
	/// <summary>
	/// Extension to add missing CopyTo.
	/// http://stackoverflow.com/a/5730893
	/// </summary>
	static internal class StreamExtension
	{
		/// <summary>
		/// Reads the bytes from the current stream and writes them to another stream.
		/// </summary>
		/// <param name="input">The input stream.</param>
		/// <param name="output">The stream to which the contents of the current stream will be copied.</param>
		public static void CopyTo(this Stream input, Stream output)
		{
			byte[] buffer = new byte[16 * 1024]; // Fairly arbitrary size
			int bytesRead;

			while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, bytesRead);
			}
		}
	}
#endif
}

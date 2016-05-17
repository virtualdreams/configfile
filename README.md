# ConfigFile

Text based configuration files based on key value pairs. Supporting:

* Singleline values
* Multiline values
* Here document mode
* Literal mode (ignoring escape characters)
* Events for add or change a key

## Description

* Lines contain a `=` are treated as a key value pair.
* Lines beginning with `#` or `;` are treated as comments, empty lines are ignored.
* The key format must be in form of `@?[a-zA-Z_][a-zA-Z0-9_.]*`.
* The value for *Here document* mode must be in form `>>[a-zA-Z][a-zA-Z0-9]*`.
* Leading and trailing whitespaces are ignored for keys and values.
* Preserve leading or trailing whitespace with surrounding double quotes.

## Example

``` c#
use ConfigFile;

namespace Demo
{
	string key;
	
	var conf = new ConfigReader("demo.conf");

	// if the key not exists, null is returned
	var v1 = conf.GetValue<string>("key", null);
	
	// if the key exists but value is <null>, an excpetion is thrown
	var v2 = conf.GetValue<string>("key", "xyz", true);
	
	// if the key not exists, an exception is thrown
	var v3 = conf.TryGetValue<string>("key");
	
	// if the key exists but value is null, an exception is thrown
	var v4 = conf.TryGetValue<string>("key", true);
	
	// test, if the key exists and write it to key
	if (conf.TryGetValue<string>("key", out key))
		Console.WriteLine("Key not exists.");
	
	// test, if the key exists but if value is <null>, an exception is thrown 
	if (conf.TryGetValue<string>("key", out key, true))
		Console.WriteLine("Key not exists.");
}
```

# Configuration examples

```
	# Standard 
	key1 = value1
	
	# Preserve whitespaces
	key2 = " value2 "
	
	# Multiline value with comment
	key3 =	value3\
			value4\
	# One more value
			value5
		
	# This is ignored
	;key4 = value6
	
	# Multiline value with preserved leading and trailing whitespace.
	key5 =	" value7, value8,\
			value9, value10\
			value11 "
			
	# This is valid, but the value is null
	key6 = 
	
	# Escape characters:
	# write newline and tab; 
	key7 = value1\n\value2\tvalue3
	# write the sequence literally.
	key8 = value1\\nvalue2\\tvalue3
	
	# Escape leading backslash and ignore continuation
	key9 = "value\\"
	
	# Here document mode
	key10 = <<EOF
		my value
	EOF
	
	# Literal mode
	@key11 = Hello\World
	
	# The following samples causes trouble.
	# Invalid key (key is null)
	 = value12
	 
	# Invalid key (contains a whitespace)
	key 12 = some value
	
	# Invalid value, empty escape sequence
	key13 = "value\"
	
	# Invalid value, unknown escape sequence (\v)
	key14 = value1\\
	value2
```
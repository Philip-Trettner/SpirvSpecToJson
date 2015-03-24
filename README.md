# SpirvSpecToJson
SPIR-V HTML Specification to JSON converter

## Direct Link

* TODO

## Supported Specs

(Currently all work-in-progress)

* [SPIRV Spec 1.0](https://www.khronos.org/registry/spir-v/specs/1.0/SPIRV.html)
* [GLSL 450](https://www.khronos.org/registry/spir-v/specs/1.0/GLSL.std.450.html)
* [OpenCL 1.2](https://www.khronos.org/registry/spir-v/specs/1.0/OpenCL.std.12.html)
* [OpenCL 2.0](https://www.khronos.org/registry/spir-v/specs/1.0/OpenCL.std.20.html)
* [OpenCL 2.1](https://www.khronos.org/registry/spir-v/specs/1.0/OpenCL.std.21.html)

(Official spec folder: https://www.khronos.org/registry/spir-v/specs/)

## JSON Format

*TODO*

### Example for OpExtInst

```
{
  "Name": "OpExtInst",
  "Description": "... description with html tags ...",
  "DescriptionPlain": "... description without html tags ...",
  "Capabilities": [],
  "WordCount": "5 + variable",
  "WordCountFix": 5,
  "OpCode": 44,
  "HasVariableWordCount": true,
  "HasResult": true,
  "HasResultType": true,
  "Operands": [
    {
      "Name": "ResultType",
      "Type": "ID"
    },
    {
      "Name": "Result",
      "Type": "ID"
    },
    {
      "Name": "Set",
      "Type": "ID"
    },
    {
      "Name": "Instruction",
      "Type": "LiteralNumber"
    },
    {
      "Name": "Operands",
      "Type": "ID[]"
    }
  ]
}
```

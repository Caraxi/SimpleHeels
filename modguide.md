Simple Heels now allows for mod developers to assign an offset directly onto the model of the footwear. This is done using the attribute fields within the `.mdl` file using the format `heels_offset=X.XXXX`.

### Assigning Offset within Simple Heels

Offsets can be assigned to model files using Simple Heels' config window on the main config page under 'Model Offset Editor'. Other mod tools will hopefully support adding the attributes with time.

### Assigning Offset with TexTools

As TexTools doesn't allow numbers in attributes, an alternative attribute format is supported. The attribute should be defined in the format of `heels_offset_(a-j)_(a-j)` where numbers are replaced with the letters `a` through `j`.

Prefixing the offset with `n_` allows negative numbers.

|    **Letter**     |    **Number**     |
|:-----------------:|:-----------------:|
|        `a`        |        `0`        |
|        `b`        |        `1`        |
|        `c`        |        `2`        |
|        `d`        |        `3`        |
|        `e`        |        `4`        |
|        `f`        |        `5`        |
|        `g`        |        `6`        |
|        `h`        |        `7`        |
|        `i`        |        `8`        |
|        `j`        |        `9`        |
|        `n`        |        `-`        |

Examples:

|       **Attribute**        | **Heel Offset**  |
|:--------------------------:|:----------------:|
|    `heels_offset_a_abf`    |     `0.015`      |
|   `heels_offset_n_a_abf`   |     `-0.015`     |
|    `heels_offset_b_abf`    |     `1.015`      |
| `heels_offset_a_bcdefghij` |  `0.123456789`   |

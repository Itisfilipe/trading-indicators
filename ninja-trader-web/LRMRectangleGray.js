const base = require("./LRMRectangle");
module.exports = {
    ...base,
    name: "LRMRectangleGray",
    description: "LRM Rectangle Gray",
    params: { ...base.params, colorPreset: { ...base.params.colorPreset, def: "grayZone" } }
};

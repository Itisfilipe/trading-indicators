const base = require("./LRMRectangle");
module.exports = {
    ...base,
    name: "LRMRectangleOrange",
    description: "LRM Rectangle Orange",
    params: { ...base.params, colorPreset: { ...base.params.colorPreset, def: "orangeZone" } }
};

const base = require("./LRMRectangle");
module.exports = {
    ...base,
    name: "LRMRectangleRed",
    description: "LRM Rectangle Red",
    params: { ...base.params, colorPreset: { ...base.params.colorPreset, def: "redZone" } }
};

const base = require("./LRMRectangle");
module.exports = {
    ...base,
    name: "LRMRectangleCyan",
    description: "LRM Rectangle Cyan",
    params: { ...base.params, colorPreset: { ...base.params.colorPreset, def: "cyanZone" } }
};

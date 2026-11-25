const base = require("./LRMRectangle");
module.exports = {
    ...base,
    name: "LRMRectanglePurple",
    description: "LRM Rectangle Purple",
    params: { ...base.params, colorPreset: { ...base.params.colorPreset, def: "purpleZone" } }
};

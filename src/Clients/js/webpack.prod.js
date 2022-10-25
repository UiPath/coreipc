const common = require('./webpack.common.js');
const { makeProd } = require('./webpack-merge-pals');

module.exports = [...common.map(makeProd)];

const common = require('./webpack.common.js');
const { makeDev } = require('./webpack-merge-pals');

module.exports = [...common.map(makeDev)];

const { merge } = require('webpack-merge');

function makeDev(config) {
    return merge(config, {
        mode: 'development',
        devtool: 'inline-source-map',
    });
}

function makeProd(config) {
    return merge(config, {
        mode: 'production',
    });
}

module.exports = { makeDev, makeProd }

//webpack.config.js
const { readFileSync } = require('fs');
const path = require('path');
const webpack = require('webpack');

const pathDist = path.resolve(__dirname, './dist');
const pathIndex = './src/';

function createConfig(params) {
    // name, target, entryFileName, augmentResolve, augmentPlugins

    const objModule = {
        rules: [
            {
                test: /\.tsx?$/,
                use: [
                    {
                        loader: 'ts-loader',
                        options: {
                            configFile: `tsconfig.${params.target}.json`,
                            allowTsInNodeModules: true,
                        },
                    },
                    {
                        loader: 'webpack-preprocessor-loader',
                        options: {
                            params: {
                                target: params.target,
                            },
                        },
                    },
                ],
            },
        ]
    };

    function createOutput() {
        return {
            path: `${pathDist}/${params.target}`,
            filename: `index.js`,
            library: {
                name: 'ipc',
                type: 'var'
            }
        };
    }

    let objResolve = {
        extensions: ['.ts', '.tsx', '.js'],
    };

    let objPlugins = [];

    if (typeof params.augmentResolve === 'function') {
        objResolve = params.augmentResolve(objResolve) ?? objResolve;
    }

    if (typeof params.augmentPlugins === 'function') {
        objPlugins = params.augmentPlugins(objPlugins) ?? objPlugins;
    }

    const RemovePlugin = require('remove-files-webpack-plugin');
    const GeneratePackageJsonPlugin = require('generate-package-json-webpack-plugin');

    const suffix = params.target === 'node' ? '' : '-web';
    const version = JSON.parse(readFileSync('package.json'))['version'];

    const basePackage = {
        'name': `coreipc${suffix}`,
        version,
        'main': './index.js',
        'typings': './index.d.ts',
        'engines': {
            'node': '>= 14'
        }
    };


    objPlugins = [
        ...objPlugins,
        new RemovePlugin({
            after: {
                test: [
                    {
                        folder: './dist/node',
                        method: (absoluteItemPath) => {
                            return new RegExp(/\.web\.d\.ts$/, 'm').test(absoluteItemPath);
                        },
                        recursive: true,
                    },
                    {
                        folder: './dist/web',
                        method: (absoluteItemPath) => {
                            return new RegExp(/\.node\.d\.ts$/, 'm').test(absoluteItemPath);
                        },
                        recursive: true,
                    },
                ],
            },
        }),
        new GeneratePackageJsonPlugin(basePackage, {
        }),
    ];

    return {
        name: params.name,
        target: params.target,
        devtool: 'inline-source-map',
        entry: `${pathIndex}${params.entryFileName}`,
        output: createOutput(),
        resolve: objResolve,
        module: objModule,
        plugins: objPlugins
    };
}

function withBuffer(configuration) {
    return {
        ...configuration,
        augmentResolve: resolve => {
            return {
                ...resolve,
                fallback: {
                    ...resolve.fallback ?? {},
                    buffer: require.resolve('buffer/'),
                }
            };
        },
        augmentPlugins: plugins => {
            return [
                ...plugins ?? [],
                new webpack.ProvidePlugin({
                    Buffer: ['buffer', 'Buffer'],
                })
            ]
        }
    };
}

module.exports = [
    createConfig({
        name: 'node',
        target: 'node',
        entryFileName: 'index.ts'
    }),

    createConfig(
        withBuffer({
            name: 'web',
            target: 'web',
            entryFileName: 'index.ts'
        })),

];

module.parallelism = 2;

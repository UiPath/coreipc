const webpack = require('webpack');
const path = require('path');
const { readFileSync } = require('fs');

const GeneratePackageJsonPlugin = require('generate-package-json-webpack-plugin');
const RemovePlugin = require('remove-files-webpack-plugin');
// const TsconfigPathsPlugin = require('tsconfig-paths-webpack-plugin');
const WebpackShellPluginNext = require('webpack-shell-plugin-next');
const CopyPlugin = require("copy-webpack-plugin");

const pathDist = path.resolve(__dirname, './dist/prepack');
const pathSrc = './src/';

function createConfig(params) {
    if (!params || !params.entry) {
        throw new Error('params.entry must be set.');
    }

    params = {
        ...params,
        target: params.target ?? 'node',
        devtool: params.devtool ?? 'source-map',
    };

    const loaders = [
        {
            loader: 'ts-loader',
            options: {
                configFile: `tsconfig.${params.name}.json`,
                allowTsInNodeModules: true,
            },
        },
        {
            loader: 'webpack-preprocessor-loader',
            options: {
                params: {
                    target: params.target,
                },
                verbose: true,
            },
        },
    ];

    let resolve = {
        extensions: ['.ts', '.tsx', '.js'],
        plugins: [
            // new TsconfigPathsPlugin({ configFile: `./tsconfig.${params.target}.json` }),
        ],
    };


    let plugins = [
    ];

    if (params.useBuffer) {
        plugins = [
            ...plugins,
            new webpack.ProvidePlugin({
                Buffer: ['buffer', 'Buffer'],
            }),
        ];

        resolve = {
            ...resolve,
            fallback: {
                ...resolve.fallback ?? {},
                buffer: require.resolve('buffer/'),
            },
        };
    }

    const removeOutliers = new RemovePlugin({
        after: {
            test: [
                {
                    folder: './dist/prepack/node',
                    method: (absoluteItemPath) => {
                        return new RegExp(/\.web\.d\.ts$/, 'm').test(absoluteItemPath);
                    },
                    recursive: true,
                },
                {
                    folder: './dist/prepack/web',
                    method: (absoluteItemPath) => {
                        return new RegExp(/\.node\.d\.ts$/, 'm').test(absoluteItemPath);
                    },
                    recursive: true,
                },
            ],
        },
    });

    const copyAssets = new CopyPlugin({
        patterns: [
            {
                from: `./assets/${params.name}/`,
                to: './',
            },
        ]
    });

    plugins = [
        ...plugins,
        removeOutliers,
        copyAssets,
    ];

    console.log('params.generatePackage === ', params.generatePackage);

    if (params.generatePackage) {
        const suffix = params.target === 'node' ? '' : '-web';
        const packageBase = JSON.parse(readFileSync('package.json'));
        const packageOverride = {
            name: `@uipath/coreipc${suffix}`,
            description: `UiPath CoreIpc for ${params.prettyTarget}`,
            typings: `./${params.target}/index.d.ts`,
        };

        const package = {
            ...packageBase,
            ...packageOverride
        };
        delete package.scripts;
        delete package.dependencies;
        delete package.devDependencies;

        const generatePackageJson = new GeneratePackageJsonPlugin(package);
        const packNpm = new WebpackShellPluginNext({
            onBuildEnd: {
                scripts: [`npm pack ./dist/prepack/${params.name} --pack-destination ./dist-packages`],
                blocking: true,
                parallel: false,
            },
        });

        plugins = [
            ...plugins,
            generatePackageJson,
            packNpm,
        ];
    }

    const module = {
        rules: [
            {
                test: /\.tsx?$/,
                use: loaders,
            },
        ],
    };

    let output = {
        path: `${pathDist}/${params.name}`,
        filename: 'index.js',
    };

    if (params.library) {
        output = {
            ...output,
            library: params.library,
        };
    }

    if (params.globalObject) {
        output = {
            ...output,
            globalObject: params.globalObject,
        };
    }

    const config = {
        mode: 'production',
        target: params.target,
        devtool: params.devtool,
        entry: params.entry,
        output,
        module,
        resolve,
        plugins
    };

    if (params.name) {
        config.name = params.name;
    }

    console.log('config.module.rules[0].use[1]', config.module.rules[0].use[1]);

    return config;
}

module.parallelism = 2;
module.exports = [
    createConfig({
        target: 'node',
        name: 'node',
        entry: `${pathSrc}node/index.ts`,
        prettyTarget: 'NodeJS',
        generatePackage: true,
        library: { name: 'ipc', type: 'umd' },
        globalObject: 'this',
    }),
    createConfig({
        target: 'web',
        name: 'web',
        entry: `${pathSrc}web/index.ts`,
        useBuffer: true,
        prettyTarget: 'Web',
        generatePackage: true,
        library: { name: 'ipc', type: 'umd' },
        globalObject: 'this',
    }),
    createConfig({
        target: 'web',
        name: 'web-js',
        entry: `${pathSrc}web/index.ts`,
        useBuffer: true,
        devtool: 'inline-source-map',
        prettyTarget: 'Web-JS',
        generatePackage: false,
        library: { name: 'ipc', type: 'var' }
    }),
];

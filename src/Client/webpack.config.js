var path = require("path");
var webpack = require("webpack");

function resolve(filePath) {
    return path.isAbsolute(filePath) ? filePath : path.join(__dirname, filePath);
}

var CONFIG = {
    fsharpEntry: './client.fsproj',
    outputDir: './public',
    babel: {
        presets: [
            ['@babel/preset-env', {
                modules: false,
                // This adds polyfills when needed. Requires core-js dependency.
                // See https://babeljs.io/docs/en/babel-preset-env#usebuiltins
                useBuiltIns: 'usage',
                corejs: 3
            }]
        ],
    }
};

var isProduction = !process.argv.find(v => v.indexOf('webpack-dev-server') !== -1);
var port = process.env.SUAVE_FABLE_PORT || "8083";
console.log("Bundling for " + (isProduction ? "production" : "development") + "...");

module.exports = {
    devtool: "source-map",
    entry: resolve(CONFIG.fsharpEntry),
    output: {
        filename: 'bundle.js',
        path: resolve(CONFIG.outputDir),
    },
    devServer: {
        proxy: [
            {
                context: ['/api/socket'],
                target: 'ws://localhost:' + port,
                ws: true
            },
            {
                context: ['/api', '/', '/logon', '/logoff', '/logonfast'],
                target: 'http://localhost:' + port,
                changeOrigin: true
            }],
        hot: true,
        inline: true
      },
    module: {
        rules: [
            {
                test: /\.fs(x|proj)?$/,
                use: {
                    loader: "fable-loader",
                    options: {
                        babel: CONFIG.babel,
                        define: isProduction ? [] : ["DEBUG"]
                    }
                }
            },
            {
                test: /\.js$/,
                exclude: /node_modules/,
                use: {
                    loader: 'babel-loader',
                    options: CONFIG.babel
                },
            },
            {
                test: /\.scss$/,
                use: [
                    "style-loader",
                    "css-loader",
                    {
                        loader: 'sass-loader',
                        options: { implementation: require('sass') }
                    }
                ]
            },
            {
                test: /\.(png|jpg|jpeg|gif|svg|woff|woff2|ttf|eot)(\?.*)?$/,
                use: ['file-loader']
            }            
        ]
    },
    plugins: isProduction ? [] : [
        new webpack.HotModuleReplacementPlugin(),
        new webpack.NamedModulesPlugin()
    ]
};

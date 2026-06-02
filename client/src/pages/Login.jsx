import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { Factory, Lock, User, ArrowRight, Settings2, Database } from 'lucide-react';
import { toast } from 'react-toastify';
import { api } from '../services/api';
import FullScreenLoader from '../components/FullScreenLoader';

const Login = () => {
    const navigate = useNavigate();
    const [loading, setLoading] = useState({ state: false, text: '' });
    const [showConfig, setShowConfig] = useState(false);
    const [credentials, setCredentials] = useState({ username: '', password: '' });
    const [dbConfig, setDbConfig] = useState({ host: '', port: '3306', database: '', user: '', password: '' });

    useEffect(() => {
        // Load config on mount
        const savedConfig = JSON.parse(localStorage.getItem('db_config') || '{}');
        if (Object.keys(savedConfig).length > 0) {
            setDbConfig({ ...dbConfig, ...savedConfig });
        }
    }, []);

    const handleLogin = async (e) => {
        e.preventDefault();
        const config = localStorage.getItem('db_config');

        if (!config) {
            toast.error('Error: Database not configured. Please click Database Configuration.');
            return;
        }

        setLoading({ state: true, text: 'Authenticating...' });

        try {
            const result = await api.login(credentials.username, credentials.password);

            if (result && result.accessToken) {
                localStorage.setItem('nexus_token', result.accessToken);
                localStorage.setItem('nexus_token_expires', result.accessExpiresAt);
                localStorage.setItem('nexus_authenticated', 'true');
                localStorage.setItem('nexus_user', result.user);
                localStorage.setItem('nexus_user_id', String(result.userId));
                toast.success('Login Successful!');
                setTimeout(() => {
                    setLoading({ state: false, text: '' });
                    navigate('/');
                }, 500);
            } else {
                setLoading({ state: false, text: '' });
                toast.error(result?.message || 'Invalid credentials.');
            }
        } catch (error) {
            setLoading({ state: false, text: '' });
            toast.error(error?.message || 'Connection to server failed.');
        }
    };

    const handleSaveConfig = async () => {
        if (!dbConfig.host || !dbConfig.database) {
            toast.error("Host and Database are required!");
            return;
        }

        setLoading({ state: true, text: 'Saving Configuration...' });
        try {
            const result = await api.saveConfig(dbConfig);
            if (result.success) {
                localStorage.setItem('db_config', JSON.stringify(dbConfig));
                toast.success(result.message);
                setShowConfig(false);
            } else {
                toast.error(result.message || "Failed to save configuration");
            }
        } catch (error) {
            toast.error('Connection to server failed.');
        } finally {
            setLoading({ state: false, text: '' });
        }
    };

    const handleTestConnection = async () => {
        if (!dbConfig.host || !dbConfig.database) {
            toast.error('Error: Database and Host are required to test.');
            return;
        }


        try {
            const result = await api.testDb(dbConfig);
            if (result.success) {
                toast.success(result.message);
            } else {
                toast.error(result.message || "Connection Failed!");
            }
        } catch (error) {
            toast.error('Connection to server failed.');
        }
    };

    return (
        <div className="flex items-center justify-center min-h-screen p-4 bg-slate-100">
            <FullScreenLoader isVisible={loading.state} text={loading.text} />

            <div className="w-full max-w-md animate-in fade-in zoom-in duration-500 relative z-10">
                <div className="text-center mb-6">
                    <div className="bg-indigo-600 w-16 h-16 rounded-2xl flex items-center justify-center mx-auto mb-4 shadow-xl shadow-indigo-200">
                        <Factory className="text-white w-8 h-8" />
                    </div>
                    <h1 className="text-3xl font-bold text-slate-900">Nexus Prod</h1>
                    <p className="text-slate-500 font-medium">Production Order Management Portal</p>
                    <p className="text-center mt-2 text-slate-400 text-sm">Part of <strong>Trader SM</strong> Windows Ecosystem</p>
                </div>

                <div className="glass-card p-8 rounded-[2.5rem] shadow-2xl shadow-slate-200 bg-white/90 backdrop-blur-md border border-white/50">
                    <form onSubmit={handleLogin} className="space-y-6">
                        <div>
                            <label className="block text-xs font-bold text-slate-400 uppercase tracking-widest mb-2 ml-1">Username</label>
                            <div className="relative">
                                <User className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-slate-400" />
                                <input
                                    type="text"
                                    required
                                    value={credentials.username}
                                    onChange={(e) => setCredentials({ ...credentials, username: e.target.value })}
                                    className="w-full pl-12 pr-4 py-4 bg-slate-50 border-2 border-transparent focus:border-indigo-500 focus:bg-white rounded-2xl transition-all outline-none text-slate-700 font-semibold"
                                    placeholder="Enter username"
                                />
                            </div>
                        </div>

                        <div>
                            <label className="block text-xs font-bold text-slate-400 uppercase tracking-widest mb-2 ml-1">Password</label>
                            <div className="relative">
                                <Lock className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-slate-400" />
                                <input
                                    type="password"
                                    required
                                    value={credentials.password}
                                    onChange={(e) => setCredentials({ ...credentials, password: e.target.value })}
                                    className="w-full pl-12 pr-4 py-4 bg-slate-50 border-2 border-transparent focus:border-indigo-500 focus:bg-white rounded-2xl transition-all outline-none text-slate-700 font-semibold"
                                    placeholder="••••••••"
                                />
                            </div>
                        </div>

                        <div className="space-y-3">
                            <button
                                type="submit"
                                className="w-full py-4 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-2xl shadow-lg shadow-indigo-200 active:scale-[0.98] transition-all flex items-center justify-center gap-2">
                                Sign In <ArrowRight className="w-5 h-5" />
                            </button>

                            <button
                                type="button"
                                onClick={() => setShowConfig(true)}
                                className="w-full py-3 flex items-center justify-center gap-2 text-slate-400 hover:text-indigo-600 font-semibold text-sm transition-colors border-none bg-transparent shadow-none">
                                <Settings2 className="w-4 h-4" />
                                Database Configuration
                            </button>
                        </div>
                    </form>
                </div>

                <p className="text-center mt-8 text-slate-400 text-sm">
                    Developed and Managed by <br /><strong>CYPHERON TECHNOLOGIES PVT. LTD</strong>
                </p>
            </div>

            {/* Config Modal */}
            {showConfig && (
                <div className="fixed inset-0 z-[100] flex items-center justify-center p-4">
                    <div className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm" onClick={() => setShowConfig(false)}></div>
                    <div className="relative bg-white w-full max-w-md rounded-[2.5rem] shadow-2xl p-8 animate-in fade-in zoom-in duration-200">
                        <div className="flex items-center gap-4 mb-6">
                            <div className="bg-indigo-50 p-3 rounded-xl text-indigo-600"><Database /></div>
                            <h3 className="text-xl font-bold text-slate-800">MySQL Connection</h3>
                        </div>

                        <div className="grid grid-cols-4 gap-4 mb-4">
                            <div className="col-span-3">
                                <label className="text-[10px] font-bold text-slate-400 uppercase ml-1">Host</label>
                                <input
                                    type="text"
                                    value={dbConfig.host}
                                    onChange={(e) => setDbConfig({ ...dbConfig, host: e.target.value })}
                                    className="w-full p-3 bg-slate-50 rounded-xl border-2 border-transparent focus:border-indigo-500 outline-none font-semibold text-sm"
                                    placeholder="localhost"
                                />
                            </div>
                            <div className="col-span-1">
                                <label className="text-[10px] font-bold text-slate-400 uppercase ml-1">Port</label>
                                <input
                                    type="number"
                                    value={dbConfig.port}
                                    onChange={(e) => setDbConfig({ ...dbConfig, port: e.target.value })}
                                    className="w-full p-3 bg-slate-50 rounded-xl border-2 border-transparent focus:border-indigo-500 outline-none font-semibold text-sm"
                                    placeholder="3306"
                                />
                            </div>
                        </div>

                        <div className="space-y-4 mb-8">
                            <div>
                                <label className="text-[10px] font-bold text-slate-400 uppercase ml-1">Database Name</label>
                                <input
                                    type="text"
                                    value={dbConfig.database}
                                    onChange={(e) => setDbConfig({ ...dbConfig, database: e.target.value })}
                                    className="w-full p-3 bg-slate-50 rounded-xl border-2 border-transparent focus:border-indigo-500 outline-none font-semibold text-sm"
                                    placeholder="sample_db"
                                />
                            </div>
                            <div>
                                <label className="text-[10px] font-bold text-slate-400 uppercase ml-1">Username</label>
                                <input
                                    type="text"
                                    value={dbConfig.user}
                                    onChange={(e) => setDbConfig({ ...dbConfig, user: e.target.value })}
                                    className="w-full p-3 bg-slate-50 rounded-xl border-2 border-transparent focus:border-indigo-500 outline-none font-semibold text-sm"
                                    placeholder="db_user"
                                />
                            </div>
                            <div>
                                <label className="text-[10px] font-bold text-slate-400 uppercase ml-1">Password</label>
                                <input
                                    type="password"
                                    value={dbConfig.password}
                                    onChange={(e) => setDbConfig({ ...dbConfig, password: e.target.value })}
                                    className="w-full p-3 bg-slate-50 rounded-xl border-2 border-transparent focus:border-indigo-500 outline-none font-semibold text-sm"
                                    placeholder="••••••••"
                                />
                            </div>
                        </div>

                        <div className="flex flex-col gap-3">
                            <div className="flex gap-3">
                                <button
                                    onClick={handleTestConnection}
                                    className="flex-1 py-3 bg-slate-100 text-slate-600 font-bold rounded-xl hover:bg-slate-200 transition-colors border-none shadow-none">
                                    Test
                                </button>
                                <button
                                    onClick={handleSaveConfig}
                                    className="flex-[2] py-3 bg-indigo-600 hover:bg-indigo-700 text-white font-bold rounded-xl shadow-lg shadow-indigo-100 active:scale-95 transition-all text-base">
                                    Save Config
                                </button>
                            </div>
                            <button
                                onClick={() => setShowConfig(false)}
                                className="w-full py-2 text-slate-400 font-semibold text-sm bg-transparent border-none shadow-none hover:text-slate-600">
                                Cancel
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default Login;

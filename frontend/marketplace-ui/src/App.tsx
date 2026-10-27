/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import Layout from './components/layout/Layout';
import LoginPage from './pages/auth/LoginPage';

const DashboardPage = lazy(() => import('./pages/dashboard/DashboardPage'));
const OrdenesPage = lazy(() => import('./pages/ordenes/OrdenesPage'));
const WmsPage = lazy(() => import('./pages/wms/WmsPage'));
const FulfillmentPage = lazy(() => import('./pages/fulfillment/FulfillmentPage'));
const RmaPage = lazy(() => import('./pages/rma/RmaPage'));
const ComprasPage = lazy(() => import('./pages/compras/ComprasPage'));
const ContabilidadPage = lazy(() => import('./pages/contabilidad/ContabilidadPage'));
const LiquidacionesPage = lazy(() => import('./pages/liquidaciones/LiquidacionesPage'));
const ProductosPage = lazy(() => import('./pages/productos/ProductosPage'));
const ReabastecimientoPage = lazy(() => import('./pages/reabastecimiento/ReabastecimientoPage'));
const ProveedoresPage = lazy(() => import('./pages/proveedores/ProveedoresPage'));
const AjustesPage = lazy(() => import('./pages/ajustes/AjustesPage'));
const ConteosPage = lazy(() => import('./pages/conteos/ConteosPage'));
const AlertasInventarioPage = lazy(() => import('./pages/alertas/AlertasInventarioPage'));
const BloqueosInventarioPage = lazy(() => import('./pages/bloqueos/BloqueosInventarioPage'));

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

function RouteLoading() {
  return (
    <div style={{ padding: 24 }} aria-live="polite" aria-busy="true">
      <div className="skeleton" style={{ height: 22, width: 220, borderRadius: 5, marginBottom: 12 }} />
      <div className="skeleton" style={{ height: 12, width: 360, maxWidth: '80%', borderRadius: 4, marginBottom: 24 }} />
      <div className="skeleton" style={{ height: 180, width: '100%', borderRadius: 8 }} />
    </div>
  );
}

function AppRoutes() {
  const { isAuthenticated } = useAuth();

  return (
    <Suspense fallback={<RouteLoading />}>
      <Routes>
        <Route path="/login" element={isAuthenticated ? <Navigate to="/dashboard" replace /> : <LoginPage />} />

        <Route path="/" element={
          <ProtectedRoute>
            <Layout />
          </ProtectedRoute>
        }>
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="dashboard" element={<DashboardPage />} />
          <Route path="ordenes" element={<OrdenesPage />} />
          <Route path="wms" element={<WmsPage />} />
          <Route path="reabastecimiento" element={<ReabastecimientoPage />} />
          <Route path="proveedores" element={<ProveedoresPage />} />
          <Route path="ajustes" element={<AjustesPage />} />
          <Route path="conteos" element={<ConteosPage />} />
          <Route path="alertas-inventario" element={<AlertasInventarioPage />} />
          <Route path="bloqueos-inventario" element={<BloqueosInventarioPage />} />
          <Route path="fulfillment" element={<FulfillmentPage />} />
          <Route path="rma" element={<RmaPage />} />
          <Route path="compras" element={<ComprasPage />} />
          <Route path="contabilidad" element={<ContabilidadPage />} />
          <Route path="liquidaciones" element={<LiquidacionesPage />} />
          <Route path="productos" element={<ProductosPage />} />
        </Route>

        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </Suspense>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider>
          <AppRoutes />
        </ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

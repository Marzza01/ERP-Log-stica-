/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ToastProvider } from './context/ToastContext';
import Layout from './components/layout/Layout';
import LoginPage from './pages/auth/LoginPage';
import DashboardPage from './pages/dashboard/DashboardPage';
import OrdenesPage from './pages/ordenes/OrdenesPage';
import WmsPage from './pages/wms/WmsPage';
import FulfillmentPage from './pages/fulfillment/FulfillmentPage';
import RmaPage from './pages/rma/RmaPage';
import ComprasPage from './pages/compras/ComprasPage';
import ContabilidadPage from './pages/contabilidad/ContabilidadPage';
import LiquidacionesPage from './pages/liquidaciones/LiquidacionesPage';
import ProductosPage from './pages/productos/ProductosPage';
import ReabastecimientoPage from './pages/reabastecimiento/ReabastecimientoPage';
import ProveedoresPage from './pages/proveedores/ProveedoresPage';
import AjustesPage from './pages/ajustes/AjustesPage';
import ConteosPage from './pages/conteos/ConteosPage';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

function AppRoutes() {
  const { isAuthenticated } = useAuth();

  return (
    <Routes>
      <Route path="/login" element={isAuthenticated ? <Navigate to="/dashboard" replace /> : <LoginPage />} />

      <Route path="/" element={
        <ProtectedRoute>
          <Layout />
        </ProtectedRoute>
      }>
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard"    element={<DashboardPage />} />
        <Route path="ordenes"      element={<OrdenesPage />} />
        <Route path="wms"          element={<WmsPage />} />
        <Route path="reabastecimiento" element={<ReabastecimientoPage />} />
        <Route path="proveedores"   element={<ProveedoresPage />} />
        <Route path="ajustes"       element={<AjustesPage />} />
        <Route path="conteos"       element={<ConteosPage />} />
        <Route path="fulfillment"  element={<FulfillmentPage />} />
        <Route path="rma"          element={<RmaPage />} />
        <Route path="compras"      element={<ComprasPage />} />
        <Route path="contabilidad" element={<ContabilidadPage />} />
        <Route path="liquidaciones" element={<LiquidacionesPage />} />
        <Route path="productos"    element={<ProductosPage />} />
      </Route>

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
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
